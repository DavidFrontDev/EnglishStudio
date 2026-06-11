using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.App.ViewModels.Listening;
using EnglishStudio.App.ViewModels.Reading;
using EnglishStudio.App.ViewModels.Speaking;
using EnglishStudio.App.ViewModels.Writing;
using EnglishStudio.App.Views.Speaking;
using EnglishStudio.App.Views.Writing;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using EnglishStudio.Modules.Ielts.Mock;
using EnglishStudio.Modules.Ielts.Speaking;
using EnglishStudio.Modules.Ielts.Writing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Mock;

/// <summary>
/// Хост-автомат полного экзамена: ведёт секции L→R→W→S. Перед каждой секцией — gate-экран, затем
/// переиспользует штатную секционную VM: Reading/Listening инлайн (UserControl), Writing/Speaking —
/// в их штатных окнах (Show() + ожидание закрытия). Слушает событие завершения секции, пишет band
/// через <see cref="IMockSessionService"/> и переходит дальше. В конце — <c>FinaliseAsync</c>.
///
/// Speaking привязан к Cambridge-тесту бандла: Part2 = SpeakingPart2BankId секции, Part1/Part3 —
/// того же теста (через <c>ISpeakingTestService.StartFullMockAsync(part2BankId)</c>).
/// </summary>
public partial class MockSessionViewModel : ObservableObject
{
    private readonly IMockSessionService _mock;
    private readonly ITestRunner _runner;
    private readonly IServiceProvider _services;
    private readonly ILogger<MockSessionViewModel> _log;

    private int _mockAttemptId;

    private static readonly MockSection[] Order =
        { MockSection.Listening, MockSection.Reading, MockSection.Writing, MockSection.Speaking };

    [ObservableProperty] private object? _currentSectionVm;
    [ObservableProperty] private string _examTitle = string.Empty;
    [ObservableProperty] private string _sectionLabel = string.Empty;
    [ObservableProperty] private int _sectionIndex;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>Экзамен закрыт: аргумент = id финализированного mock (для разбора в Шаге 8) или null.</summary>
    public event Action<int?>? Closed;

    public MockSessionViewModel(
        IMockSessionService mock,
        ITestRunner runner,
        IServiceProvider services,
        ILogger<MockSessionViewModel> log)
    {
        _mock = mock;
        _runner = runner;
        _services = services;
        _log = log;
    }

    private static int IndexOf(MockSection s) => Array.IndexOf(Order, s) + 1;
    private static string NameOf(MockSection s) => s.ToString();
    private static bool IsWindowed(MockSection s) => s is MockSection.Writing or MockSection.Speaking;

    public async Task StartAsync(int mockAttemptId)
    {
        _mockAttemptId = mockAttemptId;
        try
        {
            var detail = await _mock.GetAsync(mockAttemptId);
            if (detail is null)
            {
                Close(null);
                return;
            }

            ExamTitle = Loc.Format("Mock_ExamTitle", (object?)detail.Summary.Book ?? string.Empty, (object?)detail.Summary.TestNumber ?? string.Empty);

            var next = detail.Summary.CurrentSection ?? FirstPending(detail);
            if (next is null)
            {
                await FinaliseAndCloseAsync();
                return;
            }
            await EnterSectionAsync(next.Value);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mock session start failed");
            StatusText = Loc.Tr("Mock_OpenExamFailed") + ex.Message;
        }
    }

    private static MockSection? FirstPending(MockAttemptDetail d) =>
        d.Sections.FirstOrDefault(s => s.Status == MockSectionStatus.Pending)?.Section;

    private async Task EnterSectionAsync(MockSection section)
    {
        try
        {
            int sourceId = await _mock.BeginSectionAsync(_mockAttemptId, section);
            SectionLabel = NameOf(section);
            SectionIndex = IndexOf(section);
            StatusText = string.Empty;

            var hint = IsWindowed(section)
                ? Loc.Tr("Mock_HintWindowed")
                : Loc.Tr("Mock_HintInline");

            (CurrentSectionVm as IDisposable)?.Dispose();
            CurrentSectionVm = new MockSectionGate(
                section, IndexOf(section), NameOf(section), hint,
                onStart: () => LaunchSectionAsync(section, sourceId),
                onSkip: () => SkipAndAdvanceAsync(section),
                onExit: () => Close(null));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Enter section {Section} failed", section);
            StatusText = Loc.Tr("Mock_OpenSectionFailed") + ex.Message;
        }
    }

    private Task LaunchSectionAsync(MockSection section, int sourceId) => section switch
    {
        MockSection.Listening => LaunchInlineListeningAsync(sourceId),
        MockSection.Reading => LaunchInlineReadingAsync(sourceId),
        MockSection.Writing => RunWritingAsync(sourceId),
        MockSection.Speaking => RunSpeakingAsync(sourceId),
        _ => Task.CompletedTask,
    };

    // ---- Inline sections (Reading / Listening) ----

    private async Task LaunchInlineListeningAsync(int testSetId)
    {
        var vm = _services.GetRequiredService<ListeningSessionViewModel>();
        vm.Finished += attemptId => _ = OnInlineFinishedAsync(MockSection.Listening, attemptId);
        vm.Cancelled += OnSectionCancelled;
        await vm.StartAsync(testSetId, trainingMode: false);
        CurrentSectionVm = vm;
    }

    private async Task LaunchInlineReadingAsync(int testSetId)
    {
        var vm = _services.GetRequiredService<ReadingTestViewModel>();
        vm.Finished += attemptId => _ = OnInlineFinishedAsync(MockSection.Reading, attemptId);
        vm.Cancelled += OnSectionCancelled;
        await vm.StartAsync(testSetId, trainingMode: false);
        CurrentSectionVm = vm;
    }

    private async Task OnInlineFinishedAsync(MockSection section, int childAttemptId)
    {
        try
        {
            var ta = await _runner.GetAsync(childAttemptId);
            await _mock.CompleteSectionAsync(_mockAttemptId, section, childAttemptId, ta?.BandEstimate);
            await AdvanceAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Complete inline section {Section} failed", section);
            StatusText = Loc.Tr("Mock_SectionCompleteError") + ex.Message;
        }
    }

    private void OnSectionCancelled() => Close(null);

    // ---- Windowed sections (Writing / Speaking) ----

    private async Task RunWritingAsync(int writingTestSetId)
    {
        CurrentSectionVm = new MockSectionRunning(Loc.Tr("Mock_WritingRunning"));

        var vm = _services.GetRequiredService<WritingSessionViewModel>();
        var window = new WritingSessionWindow { DataContext = vm, Owner = Application.Current.MainWindow };

        IReadOnlyList<int>? submitted = null;
        IReadOnlyList<int>? cancelled = null;
        var closedTcs = new TaskCompletionSource();

        vm.Submitted += ids => { submitted = ids; window.Close(); };
        vm.Cancelled += ids => { cancelled = ids; window.Close(); };
        window.Closed += (_, _) => { vm.Cleanup(); closedTcs.TrySetResult(); };

        await vm.StartAsync(writingTestSetId);
        window.Show();
        await closedTcs.Task;

        if (submitted is { Count: > 0 } ids)
        {
            var band = await EvaluateWritingAsync(ids);
            // FK хранит Task1 (ids[0]); Task2 (ids[1]) — в SecondaryChildAttemptId, чтобы разбор
            // секции открыл обе задачи. Band секции — взвешенный (закэширован).
            int? task2Id = ids.Count > 1 ? ids[1] : null;
            await _mock.CompleteSectionAsync(_mockAttemptId, MockSection.Writing, ids[0], band, task2Id);
            await AdvanceAsync();
        }
        else
        {
            await DiscardWritingAttemptsAsync(cancelled);
            Close(null);
        }
    }

    private async Task<double?> EvaluateWritingAsync(IReadOnlyList<int> attemptIds)
    {
        var feedback = _services.GetRequiredService<WritingFeedbackService>();
        var taskSvc = _services.GetRequiredService<IWritingTaskService>();

        var aiVm = new AiProcessingViewModel();
        aiVm.Start();
        var aiWindow = new AiProcessingWindow { DataContext = aiVm, Owner = Application.Current.MainWindow };
        aiWindow.Show();
        try
        {
            for (var i = 0; i < attemptIds.Count; i++)
            {
                aiVm.StatusText = Loc.Format("Mock_EvaluatingTask", i + 1, attemptIds.Count);
                await feedback.EvaluateAndSaveAsync(attemptIds[i]);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Writing evaluation in mock failed");
        }
        finally
        {
            aiVm.Stop();
            aiWindow.Close();
        }

        double? t1 = (await taskSvc.GetAttemptAsync(attemptIds[0]))?.BandOverall;
        double? t2 = attemptIds.Count > 1 ? (await taskSvc.GetAttemptAsync(attemptIds[1]))?.BandOverall : null;
        return WeightedWritingBand(t1, t2);
    }

    /// <summary>IELTS Writing: Task1·1/3 + Task2·2/3, округление к ближайшей половине (как WritingResultViewModel).</summary>
    private static double? WeightedWritingBand(double? task1, double? task2)
    {
        if (task1 is null && task2 is null) return null;
        var t1 = task1 ?? task2 ?? 0;
        var t2 = task2 ?? task1 ?? 0;
        var raw = (t1 + 2 * t2) / 3.0;
        return Math.Round(raw * 2) / 2;
    }

    private async Task DiscardWritingAttemptsAsync(IReadOnlyList<int>? attemptIds)
    {
        if (attemptIds is null) return;
        var taskSvc = _services.GetRequiredService<IWritingTaskService>();
        foreach (var id in attemptIds)
        {
            try { await taskSvc.DeleteAttemptAsync(id); }
            catch (Exception ex) { _log.LogWarning(ex, "Discard writing attempt {Id} failed", id); }
        }
    }

    private async Task RunSpeakingAsync(int part2BankId)
    {
        CurrentSectionVm = new MockSectionRunning(Loc.Tr("Mock_SpeakingRunning"));

        var vm = _services.GetRequiredService<SpeakingSessionViewModel>();
        var window = new SpeakingSessionWindow { DataContext = vm, Owner = Application.Current.MainWindow };

        int? submittedId = null;
        int? cancelledId = null;
        var closedTcs = new TaskCompletionSource();

        vm.Submitted += aid => { submittedId = aid; window.Close(); };
        vm.Cancelled += aid => { cancelledId = aid; window.Close(); };
        window.Closed += (_, _) => { vm.Cleanup(); closedTcs.TrySetResult(); };

        // FullMock привязан к Cambridge-тесту бандла: Part2 = sourceId (SpeakingPart2BankId),
        // Part1/Part3 — того же теста. При отсутствии банка сервис деградирует к случайному набору.
        await vm.StartAsync(SpeakingMode.FullMock, part1BankId: null, part2BankId: part2BankId);
        window.Show();
        await closedTcs.Task;

        if (submittedId is int sid)
        {
            // Speaking сам прогоняет AI-оценку до Submitted — band уже сохранён.
            var spk = _services.GetRequiredService<ISpeakingTestService>();
            var detail = await spk.GetAttemptAsync(sid);
            await _mock.CompleteSectionAsync(_mockAttemptId, MockSection.Speaking, sid, detail?.Summary.BandOverall);
            await AdvanceAsync();
        }
        else
        {
            if (cancelledId is int cid)
            {
                try { await _services.GetRequiredService<ISpeakingTestService>().DeleteAttemptAsync(cid); }
                catch (Exception ex) { _log.LogWarning(ex, "Discard speaking attempt {Id} failed", cid); }
            }
            Close(null);
        }
    }

    // ---- Shared flow ----

    private async Task SkipAndAdvanceAsync(MockSection section)
    {
        try
        {
            await _mock.SkipSectionAsync(_mockAttemptId, section, "skipped (v1)");
            await AdvanceAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Skip section {Section} failed", section);
            StatusText = Loc.Tr("Mock_SkipSectionError") + ex.Message;
        }
    }

    private async Task AdvanceAsync()
    {
        var detail = await _mock.GetAsync(_mockAttemptId);
        var next = detail?.Summary.CurrentSection;
        if (next is null)
        {
            await FinaliseAndCloseAsync();
            return;
        }
        await EnterSectionAsync(next.Value);
    }

    private async Task FinaliseAndCloseAsync()
    {
        try
        {
            await _mock.FinaliseAsync(_mockAttemptId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Finalise mock failed");
        }
        Close(_mockAttemptId);
    }

    private void Close(int? finalisedMockAttemptId)
    {
        (CurrentSectionVm as IDisposable)?.Dispose();
        Closed?.Invoke(finalisedMockAttemptId);
    }
}

/// <summary>
/// Gate-экран перед секцией (он же межсекционный переход): «готовы начать секцию X».
/// «Пропустить» делает mock частичным; «Выйти в меню» возвращает в Hub (экзамен resumable).
/// </summary>
public sealed partial class MockSectionGate : ObservableObject
{
    private readonly Func<Task> _onStart;
    private readonly Func<Task> _onSkip;
    private readonly Action _onExit;

    public MockSection Section { get; }
    public int Index { get; }
    public string SectionName { get; }
    public string Hint { get; }

    public MockSectionGate(
        MockSection section, int index, string sectionName, string hint,
        Func<Task> onStart, Func<Task> onSkip, Action onExit)
    {
        Section = section;
        Index = index;
        SectionName = sectionName;
        Hint = hint;
        _onStart = onStart;
        _onSkip = onSkip;
        _onExit = onExit;
    }

    [RelayCommand]
    private async Task Start() => await _onStart();

    [RelayCommand]
    private async Task Skip() => await _onSkip();

    [RelayCommand]
    private void Exit() => _onExit();
}

/// <summary>Неинтерактивный экран на время прохождения секции в отдельном окне (Writing/Speaking).</summary>
public sealed class MockSectionRunning
{
    public string Message { get; }
    public MockSectionRunning(string message) => Message = message;
}
