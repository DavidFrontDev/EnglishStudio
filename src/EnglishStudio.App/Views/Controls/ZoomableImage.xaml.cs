using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EnglishStudio.App.Views.Dialogs;

namespace EnglishStudio.App.Views.Controls;

/// <summary>
/// Изображение в заданиях L/R/W с увеличением: при наведении показывает «лупу», по левому клику
/// открывает <see cref="ImageZoomWindow"/> (200%). Drop-in замена <c>&lt;Image&gt;</c> —
/// <see cref="Source"/> принимает путь-строку (как <c>ImagePath</c>), <see cref="Stretch"/>/
/// <see cref="StretchDirection"/>/<see cref="MaxImageHeight"/>/<see cref="MaxImageWidth"/> повторяют
/// одноимённые свойства внутреннего <c>Image</c>.
/// </summary>
public partial class ZoomableImage : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(ZoomableImage), new PropertyMetadata(null));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(Stretch), typeof(ZoomableImage), new PropertyMetadata(Stretch.Uniform));

    public static readonly DependencyProperty StretchDirectionProperty = DependencyProperty.Register(
        nameof(StretchDirection), typeof(StretchDirection), typeof(ZoomableImage), new PropertyMetadata(StretchDirection.Both));

    public static readonly DependencyProperty MaxImageHeightProperty = DependencyProperty.Register(
        nameof(MaxImageHeight), typeof(double), typeof(ZoomableImage), new PropertyMetadata(double.PositiveInfinity));

    public static readonly DependencyProperty MaxImageWidthProperty = DependencyProperty.Register(
        nameof(MaxImageWidth), typeof(double), typeof(ZoomableImage), new PropertyMetadata(double.PositiveInfinity));

    /// <summary>Путь к файлу изображения (как привязка к <c>ImagePath</c>).</summary>
    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Stretch Stretch { get => (Stretch)GetValue(StretchProperty); set => SetValue(StretchProperty, value); }
    public StretchDirection StretchDirection { get => (StretchDirection)GetValue(StretchDirectionProperty); set => SetValue(StretchDirectionProperty, value); }
    public double MaxImageHeight { get => (double)GetValue(MaxImageHeightProperty); set => SetValue(MaxImageHeightProperty, value); }
    public double MaxImageWidth { get => (double)GetValue(MaxImageWidthProperty); set => SetValue(MaxImageWidthProperty, value); }

    public ZoomableImage()
    {
        InitializeComponent();
    }

    private void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Source) || !File.Exists(Source)) return;
        ImageZoomWindow.Show(Source, Window.GetWindow(this));
    }
}
