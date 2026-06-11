using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Reading.Services;

namespace EnglishStudio.App.Views.ReadingStudy;

/// <summary>
/// Bare-bones WPM trend line chart (no charting NuGet) — modelled on the Writing module's
/// BandTrendChart. Y axis auto-scales to the data; X is categorical with date ticks.
/// Re-renders when <see cref="Points"/> (an <see cref="ReadingSpeedPoint"/> sequence) or the
/// control size changes.
/// </summary>
public partial class ReadingSpeedChart : UserControl
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IEnumerable),
        typeof(ReadingSpeedChart),
        new PropertyMetadata(null, OnPointsChanged));

    public IEnumerable? Points
    {
        get => (IEnumerable?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private const double LeftPadding = 40;
    private const double RightPadding = 16;
    private const double TopPadding = 16;
    private const double BottomPadding = 32;

    public ReadingSpeedChart()
    {
        InitializeComponent();
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (ReadingSpeedChart)d;

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

        var points = Points?.Cast<ReadingSpeedPoint>().ToList() ?? new List<ReadingSpeedPoint>();

        if (points.Count == 0)
        {
            EmptyLabel.Visibility = Visibility.Visible;
            return;
        }
        EmptyLabel.Visibility = Visibility.Collapsed;

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 80) return;

        var plotW = width - LeftPadding - RightPadding;
        var plotH = height - TopPadding - BottomPadding;
        if (plotW <= 0 || plotH <= 0) return;

        // Y axis: 0..nice ceiling above the max WPM.
        var maxWpm = Math.Max(20, points.Max(p => p.Wpm));
        var axisMax = Math.Ceiling(maxWpm / 20.0) * 20.0;

        DrawGridAndAxes(plotW, plotH, axisMax);

        var n = points.Count;
        var stepX = n == 1 ? 0 : plotW / (n - 1);

        Point XYFor(int i, double wpm)
        {
            var x = LeftPadding + (n == 1 ? plotW / 2.0 : i * stepX);
            var y = TopPadding + plotH - wpm / axisMax * plotH;
            return new Point(x, y);
        }

        var stroke = TryFindResource("AccentHotBrush") as Brush ?? Brushes.OrangeRed;

        var line = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = 2.4,
            StrokeLineJoin = PenLineJoin.Round
        };
        for (var i = 0; i < n; i++) line.Points.Add(XYFor(i, points[i].Wpm));
        PlotCanvas.Children.Add(line);

        for (var i = 0; i < n; i++)
        {
            var p = XYFor(i, points[i].Wpm);
            var dot = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = stroke,
                ToolTip = Loc.Format("Reader_WpmTooltip", points[i].Date.ToLocalTime().ToString("dd MMM yyyy"), points[i].Wpm, points[i].AccuracyPct)
            };
            Canvas.SetLeft(dot, p.X - 4);
            Canvas.SetTop(dot, p.Y - 4);
            PlotCanvas.Children.Add(dot);

            var label = new TextBlock
            {
                Text = points[i].Wpm.ToString("F0", CultureInfo.InvariantCulture),
                Foreground = TryFindResource("StrongTextBrush") as Brush ?? Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, p.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, Math.Max(0, p.Y - 22));
            PlotCanvas.Children.Add(label);

            var tick = new TextBlock
            {
                Text = points[i].Date.ToLocalTime().ToString("dd.MM", CultureInfo.InvariantCulture),
                Foreground = TryFindResource("MutedTextBrush") as Brush ?? Brushes.Gray,
                FontSize = 10
            };
            tick.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tick, p.X - tick.DesiredSize.Width / 2);
            Canvas.SetTop(tick, TopPadding + plotH + 6);
            PlotCanvas.Children.Add(tick);
        }
    }

    private void DrawGridAndAxes(double plotW, double plotH, double axisMax)
    {
        var gridBrush = TryFindResource("OverlayLight20Brush") as Brush ?? Brushes.DimGray;
        var labelBrush = TryFindResource("MutedTextBrush") as Brush ?? Brushes.Gray;

        const int divisions = 4;
        for (var k = 0; k <= divisions; k++)
        {
            var value = axisMax * k / divisions;
            var y = TopPadding + plotH - value / axisMax * plotH;
            var line = new Line
            {
                X1 = LeftPadding,
                X2 = LeftPadding + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = k == 0 ? 1.2 : 0.6,
                StrokeDashArray = k == 0 ? null : new DoubleCollection(new double[] { 2, 3 })
            };
            PlotCanvas.Children.Add(line);

            var lbl = new TextBlock
            {
                Text = value.ToString("F0", CultureInfo.InvariantCulture),
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
