using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EnglishStudio.App.ViewModels.Writing;

namespace EnglishStudio.App.Views.Writing;

/// <summary>
/// Bare-bones line chart for weekly band-overall trend. Draws axes (Y: 0–9 bands),
/// horizontal gridlines, a polyline through the points, and dots with band-value labels.
/// Re-renders whenever <see cref="Points"/> or the control size changes.
/// </summary>
public partial class BandTrendChart : UserControl
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IEnumerable),
        typeof(BandTrendChart),
        new PropertyMetadata(null, OnPointsChanged));

    public IEnumerable? Points
    {
        get => (IEnumerable?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private const double AxisMin = 0.0;
    private const double AxisMax = 9.0;
    private const double LeftPadding = 36;
    private const double RightPadding = 16;
    private const double TopPadding = 16;
    private const double BottomPadding = 32;

    public BandTrendChart()
    {
        InitializeComponent();
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (BandTrendChart)d;

        if (e.OldValue is INotifyCollectionChanged oldNcc)
            oldNcc.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newNcc)
            newNcc.CollectionChanged += chart.OnCollectionChanged;

        chart.Redraw();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        PlotCanvas.Children.Clear();

        var points = Points?.Cast<WritingHistoryViewModel.BandTrendPoint>().ToList()
                     ?? new List<WritingHistoryViewModel.BandTrendPoint>();

        if (points.Count == 0)
        {
            EmptyLabel.Visibility = Visibility.Visible;
            return;
        }
        EmptyLabel.Visibility = Visibility.Collapsed;

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 80) return; // initial layout pass

        var plotW = width - LeftPadding - RightPadding;
        var plotH = height - TopPadding - BottomPadding;
        if (plotW <= 0 || plotH <= 0) return;

        DrawGridAndAxes(plotW, plotH);

        // X-coordinates: evenly spaced left-to-right (we draw a categorical line, not
        // time-proportional, which is more legible when weeks are sparse and irregular).
        var n = points.Count;
        var stepX = n == 1 ? 0 : plotW / (n - 1);

        Point XYFor(int i, double band)
        {
            var x = LeftPadding + (n == 1 ? plotW / 2.0 : i * stepX);
            var y = TopPadding + plotH - (band - AxisMin) / (AxisMax - AxisMin) * plotH;
            return new Point(x, y);
        }

        // Polyline.
        var line = new Polyline
        {
            Stroke = TryFindResource("AccentHotBrush") as Brush ?? Brushes.OrangeRed,
            StrokeThickness = 2.4,
            StrokeLineJoin = PenLineJoin.Round
        };
        for (var i = 0; i < n; i++) line.Points.Add(XYFor(i, points[i].AverageBand));
        PlotCanvas.Children.Add(line);

        // Dots + labels.
        for (var i = 0; i < n; i++)
        {
            var p = XYFor(i, points[i].AverageBand);
            var dot = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = line.Stroke,
                ToolTip = $"{points[i].WeekStart:dd MMM yyyy}: band {points[i].AverageBand:F1} · {points[i].AttemptCount} попыток"
            };
            Canvas.SetLeft(dot, p.X - 4);
            Canvas.SetTop(dot, p.Y - 4);
            PlotCanvas.Children.Add(dot);

            var label = new TextBlock
            {
                Text = points[i].AverageBand.ToString("F1", CultureInfo.InvariantCulture),
                Foreground = TryFindResource("StrongTextBrush") as Brush ?? Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, p.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, p.Y - 22);
            PlotCanvas.Children.Add(label);

            // Date tick beneath the dot.
            var tick = new TextBlock
            {
                Text = points[i].WeekStart.ToString("dd.MM", CultureInfo.InvariantCulture),
                Foreground = TryFindResource("MutedTextBrush") as Brush ?? Brushes.Gray,
                FontSize = 10
            };
            tick.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tick, p.X - tick.DesiredSize.Width / 2);
            Canvas.SetTop(tick, TopPadding + plotH + 6);
            PlotCanvas.Children.Add(tick);
        }
    }

    private void DrawGridAndAxes(double plotW, double plotH)
    {
        var gridBrush = TryFindResource("OverlayLight20Brush") as Brush ?? Brushes.DimGray;
        var labelBrush = TryFindResource("MutedTextBrush") as Brush ?? Brushes.Gray;

        // Horizontal gridlines at every 1.0 band, with labels on the left.
        for (var band = (int)AxisMin; band <= (int)AxisMax; band++)
        {
            var y = TopPadding + plotH - (band - AxisMin) / (AxisMax - AxisMin) * plotH;
            var line = new Line
            {
                X1 = LeftPadding,
                X2 = LeftPadding + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = band == 0 ? 1.2 : 0.6,
                StrokeDashArray = band == 0 ? null : new DoubleCollection(new double[] { 2, 3 })
            };
            PlotCanvas.Children.Add(line);

            var lbl = new TextBlock
            {
                Text = band.ToString(CultureInfo.InvariantCulture),
                Foreground = labelBrush,
                FontSize = 10
            };
            lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(lbl, LeftPadding - lbl.DesiredSize.Width - 6);
            Canvas.SetTop(lbl, y - lbl.DesiredSize.Height / 2);
            PlotCanvas.Children.Add(lbl);
        }
    }
}
