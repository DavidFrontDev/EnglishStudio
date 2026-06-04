using EnglishStudio.Integration.Tests.Infrastructure;
using EnglishStudio.Modules.Ielts.Listening;
using EnglishStudio.Modules.Ielts.Mock;
using EnglishStudio.Modules.Ielts.Reading;
using EnglishStudio.Modules.Ielts.Speaking;
using EnglishStudio.Modules.Ielts.Writing;
using Xunit;

namespace EnglishStudio.Integration.Tests.Mock;

/// <summary>
/// Интеграционные тесты оркестратора поверх SQLite in-memory (FK off, childAttemptId фейковые).
/// Покрывают старт (4 секции, текущая = Listening, Skipped при отсутствии контента), финализацию
/// (overall через официальный калькулятор на 4 секциях; среднее + IsPartial при &lt;4) и хранение
/// обоих Writing-attempt'ов (опция A).
/// </summary>
public class MockSessionServiceTests
{
    private static MockBundleSummary Bundle(
        int? listening = 10, int? reading = 20, int? writing = 30, int speaking = 40,
        int book = 15, int test = 1)
    {
        var available = 1
            + (listening.HasValue ? 1 : 0)
            + (reading.HasValue ? 1 : 0)
            + (writing.HasValue ? 1 : 0);
        return new MockBundleSummary(book, test, listening, reading, writing, speaking, available);
    }

    private static MockSessionService NewService(SqliteInMemoryDb fx)
    {
        // Секционные сервисы — бросающие двойники: FinaliseAsync с закэшированными band'ами их не дёргает.
        var picker = new MockBundlePicker(
            new FakeSpeakingTestService(Array.Empty<SpeakingTopicSummary>()),
            new FakeListeningTestService(Array.Empty<ListeningTestSummary>()),
            new FakeReadingTestService(Array.Empty<ReadingTestSummary>()),
            new FakeWritingTaskService(Array.Empty<WritingTestSetSummary>()));

        return new MockSessionService(
            fx.Factory,
            picker,
            new FakeTestRunner(),
            new FakeWritingTaskService(Array.Empty<WritingTestSetSummary>()),
            new FakeSpeakingTestService(Array.Empty<SpeakingTopicSummary>()));
    }

    [Fact]
    public async Task StartAttemptAsync_creates_four_pending_sections_with_listening_current()
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        var id = await svc.StartAttemptAsync(MockMode.CambridgeBundle, Bundle());
        var d = await svc.GetAsync(id);

        Assert.NotNull(d);
        Assert.Equal(
            new[] { MockSection.Listening, MockSection.Reading, MockSection.Writing, MockSection.Speaking },
            d!.Sections.Select(s => s.Section));
        Assert.All(d.Sections, s => Assert.Equal(MockSectionStatus.Pending, s.Status));
        Assert.All(d.Sections, s => Assert.Null(s.ChildAttemptId));
        Assert.Equal(MockSection.Listening, d.Summary.CurrentSection);
        Assert.False(d.Summary.IsPartial);
        Assert.Null(d.Summary.OverallBand);
    }

    [Fact]
    public async Task StartAttemptAsync_section_without_source_is_skipped_and_current_advances()
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        var id = await svc.StartAttemptAsync(MockMode.RandomMix, Bundle(listening: null));
        var d = await svc.GetAsync(id);

        var listening = d!.Sections.Single(s => s.Section == MockSection.Listening);
        Assert.Equal(MockSectionStatus.Skipped, listening.Status);
        Assert.Equal(MockSection.Reading, d.Summary.CurrentSection);
    }

    [Fact]
    public async Task StartAttemptAsync_null_bundle_throws()
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => svc.StartAttemptAsync(MockMode.CambridgeBundle, null));
    }

    [Fact]
    public async Task CompleteSectionAsync_advances_current_to_next_pending()
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        var id = await svc.StartAttemptAsync(MockMode.CambridgeBundle, Bundle());
        await svc.CompleteSectionAsync(id, MockSection.Listening, childAttemptId: 1, band: 7.0);

        var d = await svc.GetAsync(id);
        Assert.Equal(MockSection.Reading, d!.Summary.CurrentSection);
        var listening = d.Sections.Single(s => s.Section == MockSection.Listening);
        Assert.Equal(MockSectionStatus.Completed, listening.Status);
        Assert.Equal(1, listening.ChildAttemptId);
        Assert.Equal(7.0, listening.Band);
    }

    [Fact]
    public async Task CompleteSectionAsync_writing_stores_both_task_attempt_ids()
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        var id = await svc.StartAttemptAsync(MockMode.CambridgeBundle, Bundle());
        await svc.CompleteSectionAsync(id, MockSection.Writing, childAttemptId: 5001, band: 6.5, secondaryChildAttemptId: 5002);

        var d = await svc.GetAsync(id);
        var writing = d!.Sections.Single(s => s.Section == MockSection.Writing);
        Assert.Equal(MockSectionStatus.Completed, writing.Status);
        Assert.Equal(5001, writing.ChildAttemptId);            // Task 1
        Assert.Equal(5002, writing.SecondaryChildAttemptId);   // Task 2 — нужен для разбора обеих задач
        Assert.Equal(5001, d.WritingAttemptId);                // FK линкуется на Task 1
    }

    [Theory]
    [InlineData(7.0, 7.0, 6.0, 6.0, 6.5)] // avg 6.50 → 6.5
    [InlineData(8.0, 7.0, 7.0, 7.0, 7.5)] // avg 7.25 → 7.5 (.25 ↑)
    [InlineData(6.0, 6.0, 6.0, 6.0, 6.0)] // avg 6.00 → 6.0
    [InlineData(6.5, 6.5, 6.0, 6.0, 6.5)] // avg 6.25 → 6.5 (.25 ↑)
    [InlineData(5.0, 6.0, 7.0, 8.0, 6.5)] // avg 6.50 → 6.5
    [InlineData(9.0, 9.0, 9.0, 9.0, 9.0)] // clamp
    public async Task FinaliseAsync_full_exam_uses_official_calculator(
        double l, double r, double w, double s, double expected)
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        var id = await svc.StartAttemptAsync(MockMode.CambridgeBundle, Bundle());
        await svc.CompleteSectionAsync(id, MockSection.Listening, 1001, l);
        await svc.CompleteSectionAsync(id, MockSection.Reading, 1002, r);
        await svc.CompleteSectionAsync(id, MockSection.Writing, 1003, w, secondaryChildAttemptId: 1004);
        await svc.CompleteSectionAsync(id, MockSection.Speaking, 1005, s);

        var overall = await svc.FinaliseAsync(id);

        Assert.Equal(expected, overall, 3);
        var d = await svc.GetAsync(id);
        Assert.False(d!.Summary.IsPartial);
        Assert.Equal(expected, d.Summary.OverallBand!.Value, 3);
        Assert.Null(d.Summary.CurrentSection);
        Assert.Equal(l, d.Summary.ListeningBand);
        Assert.Equal(r, d.Summary.ReadingBand);
        Assert.Equal(w, d.Summary.WritingBand);
        Assert.Equal(s, d.Summary.SpeakingBand);
    }

    [Fact]
    public async Task FinaliseAsync_partial_averages_available_and_marks_partial()
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        var id = await svc.StartAttemptAsync(MockMode.CambridgeBundle, Bundle());
        await svc.CompleteSectionAsync(id, MockSection.Listening, 2001, 7.0);
        await svc.CompleteSectionAsync(id, MockSection.Reading, 2002, 6.0);
        await svc.SkipSectionAsync(id, MockSection.Writing, "test");
        await svc.SkipSectionAsync(id, MockSection.Speaking, "test");

        var overall = await svc.FinaliseAsync(id);

        Assert.Equal(6.5, overall, 3); // avg(7,6)=6.5 → official 6.5
        var d = await svc.GetAsync(id);
        Assert.True(d!.Summary.IsPartial);
        Assert.Equal(6.5, d.Summary.OverallBand!.Value, 3);
        Assert.Null(d.Summary.WritingBand);
        Assert.Null(d.Summary.SpeakingBand);
    }

    [Fact]
    public async Task FinaliseAsync_three_sections_marks_partial()
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        var id = await svc.StartAttemptAsync(MockMode.CambridgeBundle, Bundle());
        await svc.CompleteSectionAsync(id, MockSection.Listening, 3001, 7.0);
        await svc.CompleteSectionAsync(id, MockSection.Reading, 3002, 6.0);
        await svc.CompleteSectionAsync(id, MockSection.Writing, 3003, 6.5, secondaryChildAttemptId: 3004);
        await svc.SkipSectionAsync(id, MockSection.Speaking, "test");

        var overall = await svc.FinaliseAsync(id);

        Assert.Equal(6.5, overall, 3); // avg(7,6,6.5)=6.5 → 6.5
        var d = await svc.GetAsync(id);
        Assert.True(d!.Summary.IsPartial);
    }

    [Fact]
    public async Task FindResumableAsync_returns_unfinished_then_null_after_finalise()
    {
        using var fx = new SqliteInMemoryDb();
        var svc = NewService(fx);

        var id = await svc.StartAttemptAsync(MockMode.CambridgeBundle, Bundle());
        var resumable = await svc.FindResumableAsync();
        Assert.NotNull(resumable);
        Assert.Equal(id, resumable!.AttemptId);

        await svc.CompleteSectionAsync(id, MockSection.Listening, 1, 7.0);
        await svc.CompleteSectionAsync(id, MockSection.Reading, 2, 7.0);
        await svc.CompleteSectionAsync(id, MockSection.Writing, 3, 7.0, secondaryChildAttemptId: 4);
        await svc.CompleteSectionAsync(id, MockSection.Speaking, 5, 7.0);
        await svc.FinaliseAsync(id);

        Assert.Null(await svc.FindResumableAsync());
    }
}
