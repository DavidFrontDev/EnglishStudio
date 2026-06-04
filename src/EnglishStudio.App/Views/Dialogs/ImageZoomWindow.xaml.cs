using System;
using System.Windows;
using System.Windows.Media.Imaging;
using EnglishStudio.App.Shell;

namespace EnglishStudio.App.Views.Dialogs;

/// <summary>
/// Окно увеличенного просмотра картинки в прокручиваемой области. Открывается на 120%, ползунок
/// внизу меняет масштаб 50…200% (картинка растёт/сжимается внутри ScrollViewer, окно не двигается).
/// Тёмная тема + хром даёт перетаскивание за заголовок и крестик закрытия (<see cref="ChromedWindow"/>).
/// Немодальное — можно держать открытым рядом с заданием.
/// </summary>
public partial class ImageZoomWindow : ChromedWindow
{
    private const double DefaultZoom = 0.5;   // 50% при открытии

    private double _baseWidth;   // натуральный размер картинки в DIP (100%)
    private double _baseHeight;

    public ImageZoomWindow(string imagePath)
    {
        InitializeComponent();
        LoadImage(imagePath);
    }

    private void LoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // декодируем сразу, не держим файл
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();

            _baseWidth = bmp.Width;
            _baseHeight = bmp.Height;
            ZoomImage.Source = bmp;
            ApplyZoom(DefaultZoom);   // подгоняет и картинку, и размер окна
        }
        catch
        {
            // Битый/недоступный файл — просто не показываем окно.
            Loaded += (_, _) => Close();
        }
    }

    private void ApplyZoom(double factor)
    {
        if (_baseWidth <= 0) return;
        ZoomImage.Width = _baseWidth * factor;
        ZoomImage.Height = _baseHeight * factor;
        if (ZoomLabel is not null) ZoomLabel.Text = $"{factor * 100:0}%";
        FitWindowToImage(factor);
    }

    /// <summary>Подгоняет окно под картинку в текущем масштабе, но не больше 92% рабочей области.</summary>
    private void FitWindowToImage(double factor)
    {
        var area = SystemParameters.WorkArea;
        const double titleBar = 40, sliderBar = 52, padding = 28;

        Width = Math.Min(_baseWidth * factor + padding, area.Width * 0.92);
        Height = Math.Min(_baseHeight * factor + titleBar + sliderBar + padding, area.Height * 0.92);

        // Если после увеличения окно вылезло за рабочую область — мягко вернуть его в видимую зону.
        // (Left/Top = NaN до показа окна — сравнения тогда false, позиция не трогается.)
        if (Left + Width > area.Right) Left = Math.Max(area.Left, area.Right - Width);
        if (Top + Height > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
    }

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ApplyZoom(e.NewValue);

    public static void Show(string imagePath, Window? owner)
    {
        var window = new ImageZoomWindow(imagePath);
        if (owner is not null) window.Owner = owner;
        window.Show();
    }
}
