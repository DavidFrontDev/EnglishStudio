using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Reading surface appearance (background tone, text colour, font size). Registered as a
/// singleton so the choice persists across reader opens within a session.
/// </summary>
public partial class ReadingAppearanceViewModel : ObservableObject
{
    public const double MinFontSize = 12;
    public const double MaxFontSize = 40;

    /// <summary>Transcription is rendered at this fraction of the main font size…</summary>
    public const double TranscriptionFontRatio = 0.6;
    /// <summary>…but never smaller than this, so it stays legible at the minimum body size.</summary>
    public const double MinTranscriptionFontSize = 8;

    [ObservableProperty] private Brush _backgroundBrush;
    [ObservableProperty] private Brush _textBrush;
    [ObservableProperty] private double _fontSize = 17;

    /// <summary>
    /// When on, the reader renders the IPA transcription above every word (smaller font).
    /// Singleton-scoped, so the choice persists across reader opens within a session.
    /// </summary>
    [ObservableProperty] private bool _showTranscription;

    /// <summary>
    /// Font size for the transcription line, derived from <see cref="FontSize"/> so it scales
    /// automatically when the reader text grows or shrinks. Bound live by the per-word units.
    /// </summary>
    public double TranscriptionFontSize => Math.Max(MinTranscriptionFontSize, FontSize * TranscriptionFontRatio);

    partial void OnFontSizeChanged(double value) => OnPropertyChanged(nameof(TranscriptionFontSize));

    public IReadOnlyList<Brush> BackgroundSwatches { get; }
    public IReadOnlyList<Brush> TextSwatches { get; }

    public ReadingAppearanceViewModel()
    {
        BackgroundSwatches = new[]
        {
            Frozen("#0B2A3D"), // тёмно-синий (под тему)
            Frozen("#14181D"), // почти чёрный
            Frozen("#2B2B2B"), // тёмно-серый
            Frozen("#3B3024"), // тёмная сепия
            Frozen("#F4ECD8"), // сепия
            Frozen("#FFFFFF"), // белый
        };

        TextSwatches = new[]
        {
            Frozen("#EAEAEA"), // светлый
            Frozen("#FFFFFF"), // белый
            Frozen("#C8D2DD"), // приглушённый
            Frozen("#5B4636"), // сепия-чернила
            Frozen("#1A1A1A"), // тёмный
            Frozen("#000000"), // чёрный
        };

        _backgroundBrush = BackgroundSwatches[0];
        _textBrush = TextSwatches[0];
    }

    [RelayCommand]
    private void SetBackground(Brush? brush)
    {
        if (brush is not null) BackgroundBrush = brush;
    }

    [RelayCommand]
    private void SetTextColor(Brush? brush)
    {
        if (brush is not null) TextBrush = brush;
    }

    [RelayCommand]
    private void IncreaseFont() => FontSize = Math.Min(MaxFontSize, FontSize + 1);

    [RelayCommand]
    private void DecreaseFont() => FontSize = Math.Max(MinFontSize, FontSize - 1);

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
