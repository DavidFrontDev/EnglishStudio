using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace EnglishStudio.App.Views.Speaking.Controls;

/// <summary>
/// Three-state recording control (Idle / Recording / Processing) reused across
/// Part 1 / Part 2 / Part 3 views. State is driven by attached dependency
/// properties so it can be bound to a VM. Clicks fire CLR events; the host VM
/// decides when to call StartRecording / StopRecording on the actual recorder.
/// </summary>
public partial class RecordingButton : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty IsRecordingProperty = DependencyProperty.Register(
        nameof(IsRecording), typeof(bool), typeof(RecordingButton),
        new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty IsProcessingProperty = DependencyProperty.Register(
        nameof(IsProcessing), typeof(bool), typeof(RecordingButton),
        new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty RemainingSecondsProperty = DependencyProperty.Register(
        nameof(RemainingSeconds), typeof(int), typeof(RecordingButton),
        new PropertyMetadata(0, OnRemainingChanged));

    public static readonly DependencyProperty MaxDurationSecondsProperty = DependencyProperty.Register(
        nameof(MaxDurationSeconds), typeof(int), typeof(RecordingButton),
        new PropertyMetadata(60));

    public static readonly DependencyProperty TranscribeProgressProperty = DependencyProperty.Register(
        nameof(TranscribeProgress), typeof(double), typeof(RecordingButton),
        new PropertyMetadata(0.0, OnProgressChanged));

    public bool IsRecording { get => (bool)GetValue(IsRecordingProperty); set => SetValue(IsRecordingProperty, value); }
    public bool IsProcessing { get => (bool)GetValue(IsProcessingProperty); set => SetValue(IsProcessingProperty, value); }
    public int RemainingSeconds { get => (int)GetValue(RemainingSecondsProperty); set => SetValue(RemainingSecondsProperty, value); }
    public int MaxDurationSeconds { get => (int)GetValue(MaxDurationSecondsProperty); set => SetValue(MaxDurationSecondsProperty, value); }

    /// <summary>Доля транскрибации 0..1. Пока 0 — модель ещё грузится, иначе показываем процент.</summary>
    public double TranscribeProgress { get => (double)GetValue(TranscribeProgressProperty); set => SetValue(TranscribeProgressProperty, value); }

    public bool ShowIdle => !IsRecording && !IsProcessing;
    public bool ShowRecording => IsRecording && !IsProcessing;
    public bool ShowProcessing => IsProcessing;

    /// <summary>Подпись в состоянии обработки: процент когда известен, иначе «готовлю модель».</summary>
    public string ProcessingCaption => TranscribeProgress > 0.0
        ? $"Транскрибирую… {TranscribeProgress * 100:0}%"
        : "Готовлю модель…";

    public event RoutedEventHandler? RecordRequested;
    public event RoutedEventHandler? StopRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public RecordingButton()
    {
        InitializeComponent();
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RecordingButton rb)
        {
            rb.PropertyChanged?.Invoke(rb, new PropertyChangedEventArgs(nameof(ShowIdle)));
            rb.PropertyChanged?.Invoke(rb, new PropertyChangedEventArgs(nameof(ShowRecording)));
            rb.PropertyChanged?.Invoke(rb, new PropertyChangedEventArgs(nameof(ShowProcessing)));
        }
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RecordingButton rb)
            rb.PropertyChanged?.Invoke(rb, new PropertyChangedEventArgs(nameof(ProcessingCaption)));
    }

    private static void OnRemainingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RecordingButton rb)
        {
            var sec = Math.Max(0, (int)e.NewValue);
            rb.RemainingLabel.Text = $"{sec / 60:D2}:{sec % 60:D2}";
        }
    }

    private void OnIdleClick(object sender, RoutedEventArgs e) =>
        RecordRequested?.Invoke(this, e);

    private void OnStopClick(object sender, RoutedEventArgs e) =>
        StopRequested?.Invoke(this, e);
}
