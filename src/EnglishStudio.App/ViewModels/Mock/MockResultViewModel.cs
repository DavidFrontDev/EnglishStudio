using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.ViewModels.Listening;
using EnglishStudio.App.ViewModels.Reading;
using EnglishStudio.App.ViewModels.Speaking;
using EnglishStudio.App.ViewModels.Writing;
using EnglishStudio.Modules.Ielts.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Mock;

/// <summary>
/// Экран результата mock-экзамена: overall band крупно, 4 sub-band (L/R/W/S), дата, пометка
/// «Частичный N/4». Для каждой пройденной секции — кнопка «Разобрать», открывающая штатный
/// секционный Result во вложенном роутере (<see cref="CurrentScreen"/>), переиспользуя существующие
/// <c>*ResultScreen</c>-обёртки и их View. Writing разбирается по обеим задачам (Task1 +
/// SecondaryChildAttemptId=Task2).
/// </summary>
public partial class MockResultViewModel : ObservableObject
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly IMockSessionService _mock;
    private readonly IServiceProvider _services;
    private readonly ILogger<MockResultViewModel> _log;

    [ObservableProperty] private string _examTitle = string.Empty;
    [ObservableProperty] private string _overallLabel = "—";
    [ObservableProperty] private bool _hasOverall;
    [ObservableProperty] private string _dateLabel = string.Empty;
    [ObservableProperty] private bool _isPartial;
    [ObservableProperty] private string _completionLabel = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>null = видна сводка-«сертификат»; иначе — вложенный секционный разбор.</summary>
    [ObservableProperty] private object? _currentScreen;

    public bool IsOverviewVisible => CurrentScreen is null;

    public ObservableCollection<MockResultSectionRow> Sections { get; } = new();

    /// <summary>«В меню» — вернуться в Hub.</summary>
    public event Action? Closed;

    public MockResultViewModel(IMockSessionService mock, IServiceProvider services, ILogger<MockResultViewModel> log)
    {
        _mock = mock;
        _services = services;
        _log = log;
    }

    partial void OnCurrentScreenChanged(object? value) => OnPropertyChanged(nameof(IsOverviewVisible));

    public async Task LoadAsync(int mockAttemptId)
    {
        StatusText = string.Empty;
        try
        {
            var detail = await _mock.GetAsync(mockAttemptId);
            if (detail is null)
            {
                StatusText = "Результат экзамена не найден.";
                return;
            }

            var s = detail.Summary;
            ExamTitle = s.Book is int b && s.TestNumber is int t
                ? $"Cambridge {b} · Test {t}"
                : "Полный экзамен";
            HasOverall = s.OverallBand.HasValue;
            OverallLabel = s.OverallBand?.ToString("0.0", Inv) ?? "—";
            DateLabel = s.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Inv);
            IsPartial = s.IsPartial;

            var scored = CountScored(s);
            CompletionLabel = s.IsPartial ? $"Частичный · {scored}/4 секций" : "Завершён · 4/4 секций";

            BuildSections(detail);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load mock result {Id}", mockAttemptId);
            StatusText = "Не удалось загрузить результат экзамена.";
        }
    }

    private static int CountScored(MockAttemptSummary s)
    {
        var n = 0;
        if (s.ListeningBand.HasValue) n++;
        if (s.ReadingBand.HasValue) n++;
        if (s.WritingBand.HasValue) n++;
        if (s.SpeakingBand.HasValue) n++;
        return n;
    }

    private void BuildSections(MockAttemptDetail detail)
    {
        Sections.Clear();
        var s = detail.Summary;
        foreach (var section in new[] { MockSection.Listening, MockSection.Reading, MockSection.Writing, MockSection.Speaking })
        {
            var state = detail.Sections.FirstOrDefault(x => x.Section == section);
            double? band = section switch
            {
                MockSection.Listening => s.ListeningBand,
                MockSection.Reading => s.ReadingBand,
                MockSection.Writing => s.WritingBand,
                MockSection.Speaking => s.SpeakingBand,
                _ => null,
            };

            Sections.Add(new MockResultSectionRow(
                Section: section,
                Name: NameOf(section),
                BandLabel: band?.ToString("0.0", Inv) ?? "—",
                StatusLabel: StatusLabelOf(state?.Status ?? MockSectionStatus.Skipped),
                ChildAttemptId: state?.ChildAttemptId,
                SecondaryChildAttemptId: state?.SecondaryChildAttemptId));
        }
    }

    [RelayCommand]
    private async Task OpenBreakdownAsync(MockResultSectionRow? row)
    {
        if (row is null || row.ChildAttemptId is not int childId) return;
        StatusText = string.Empty;
        try
        {
            object? screen = row.Section switch
            {
                MockSection.Listening => await BuildListeningScreenAsync(childId),
                MockSection.Reading => await BuildReadingScreenAsync(childId),
                MockSection.Writing => await BuildWritingScreenAsync(childId, row.SecondaryChildAttemptId),
                MockSection.Speaking => await BuildSpeakingScreenAsync(childId),
                _ => null,
            };
            if (screen is not null) CurrentScreen = screen;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to open {Section} breakdown (attempt {Id})", row.Section, childId);
            StatusText = $"Не удалось открыть разбор секции {row.Name}.";
        }
    }

    [RelayCommand]
    private void BackToHub() => Closed?.Invoke();

    // ---- per-section breakdown screens (reuse existing module Result wrappers) ----

    private async Task<object> BuildListeningScreenAsync(int attemptId)
    {
        var vm = _services.GetRequiredService<ListeningResultViewModel>();
        await vm.LoadAsync(attemptId);
        return new ListeningResultScreen(vm, BackToOverviewAsync);
    }

    private async Task<object> BuildReadingScreenAsync(int attemptId)
    {
        var vm = _services.GetRequiredService<ReadingResultViewModel>();
        await vm.LoadAsync(attemptId);
        return new ReadingResultScreen(vm, BackToOverviewAsync);
    }

    private async Task<object> BuildSpeakingScreenAsync(int attemptId)
    {
        var vm = _services.GetRequiredService<SpeakingResultViewModel>();
        await vm.LoadAsync(attemptId);
        // Останавливаем воспроизведение ответов при возврате к сводке.
        return new SpeakingResultScreen(vm, () => { vm.StopAllPlayback(); return BackToOverviewAsync(); });
    }

    private async Task<object> BuildWritingScreenAsync(int task1AttemptId, int? task2AttemptId)
    {
        var vm = _services.GetRequiredService<WritingResultViewModel>();
        var ids = task2AttemptId is int t2 ? new[] { task1AttemptId, t2 } : new[] { task1AttemptId };
        await vm.LoadAsync(ids);
        return new WritingResultScreen(vm, BackToOverviewAsync);
    }

    private Task BackToOverviewAsync()
    {
        CurrentScreen = null;
        return Task.CompletedTask;
    }

    private static string NameOf(MockSection s) => s switch
    {
        MockSection.Listening => "Listening",
        MockSection.Reading => "Reading",
        MockSection.Writing => "Writing",
        MockSection.Speaking => "Speaking",
        _ => s.ToString(),
    };

    private static string StatusLabelOf(MockSectionStatus s) => s switch
    {
        MockSectionStatus.Completed => "завершена",
        MockSectionStatus.Skipped => "пропущена",
        MockSectionStatus.InProgress => "не завершена",
        MockSectionStatus.Failed => "ошибка",
        _ => "не пройдена",
    };
}

/// <summary>Строка-результат секции в сводке mock'а. «Разобрать» доступен только при наличии attempt'а.</summary>
public sealed record MockResultSectionRow(
    MockSection Section,
    string Name,
    string BandLabel,
    string StatusLabel,
    int? ChildAttemptId,
    int? SecondaryChildAttemptId)
{
    public bool CanReview => ChildAttemptId.HasValue;
}
