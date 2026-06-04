using EnglishStudio.Integration.Tests.Infrastructure;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Listening;
using EnglishStudio.Modules.Ielts.Mock;
using EnglishStudio.Modules.Ielts.Reading;
using EnglishStudio.Modules.Ielts.Speaking;
using EnglishStudio.Modules.Ielts.Writing;
using Xunit;

namespace EnglishStudio.Integration.Tests.Mock;

/// <summary>
/// Чистые тесты сборки бандлов: picker матчит Listening/Reading по book/test из Speaking Part2-якоря,
/// Writing раздаёт ротацией, Reading без book-совпадения тоже падает в ротацию. БД не нужна —
/// сервисы заменены ручными двойниками с канонными данными.
/// </summary>
public class MockBundlePickerTests
{
    private static SpeakingTopicSummary Part2(int bankId, int book, int test) =>
        new(bankId, SpeakingPart.Part2, $"cambridge-{book}-test-{test}-part-2",
            $"Cambridge {book} Test {test} — Part 2", "Describe something.", QuestionCount: 1);

    private static ListeningTestSummary Listening(int id, string code) =>
        new(id, code, $"Listening {code}", IeltsTestMode.Academic, PartCount: 4, QuestionCount: 40, LastBand: null, IsExamOnly: false);

    private static ReadingTestSummary Reading(int id, string code) =>
        new(id, code, $"Reading {code}", IeltsTestMode.Academic, PartCount: 3, QuestionCount: 40, LastBand: null, IsExamOnly: false);

    private static WritingTestSetSummary Writing(int id, string code) =>
        new(id, code, $"Writing {code}", Attribution: null,
            Task1Id: id * 10, Task1PromptPreview: "T1", WritingChartType.BarChart,
            Task2Id: id * 10 + 1, Task2PromptPreview: "T2", Task2TopicCategory: null,
            CompletedAttempts: 0, LastOverallBand: null);

    // Writing с book/test в Title — как реальные наборы ("IELTS Test Book 15, Test 1").
    private static WritingTestSetSummary WritingTagged(int id, int book, int test) =>
        new(id, $"acad-w-test{id:D2}", $"IELTS Test Book {book}, Test {test}",
            Attribution: $"IELTS Test Book {book} Academic - Test {test}",
            Task1Id: id * 10, Task1PromptPreview: "T1", WritingChartType.BarChart,
            Task2Id: id * 10 + 1, Task2PromptPreview: "T2", Task2TopicCategory: null,
            CompletedAttempts: 0, LastOverallBand: null);

    private static MockBundlePicker BuildPicker(
        IReadOnlyList<SpeakingTopicSummary> p2,
        IReadOnlyList<ListeningTestSummary> listening,
        IReadOnlyList<ReadingTestSummary> reading,
        IReadOnlyList<WritingTestSetSummary> writing)
        => new(
            new FakeSpeakingTestService(p2),
            new FakeListeningTestService(listening),
            new FakeReadingTestService(reading),
            new FakeWritingTaskService(writing));

    [Fact]
    public async Task ListAsync_one_bundle_per_part2_anchor_sorted_by_book_test()
    {
        // Якоря намеренно в неотсортированном порядке — picker должен вернуть их по (book, test).
        var picker = BuildPicker(
            p2: new[] { Part2(102, 16, 2), Part2(101, 15, 1) },
            listening: Array.Empty<ListeningTestSummary>(),
            reading: Array.Empty<ReadingTestSummary>(),
            writing: Array.Empty<WritingTestSetSummary>());

        var bundles = await picker.ListAsync();

        Assert.Equal(2, bundles.Count);
        Assert.Equal((15, 1), (bundles[0].Book, bundles[0].TestNumber));
        Assert.Equal((16, 2), (bundles[1].Book, bundles[1].TestNumber));
        Assert.Equal(101, bundles[0].SpeakingPart2BankId);
        Assert.Equal(102, bundles[1].SpeakingPart2BankId);
    }

    [Fact]
    public async Task ListAsync_matches_listening_and_reading_by_book_test_and_rotates_writing()
    {
        var picker = BuildPicker(
            p2: new[] { Part2(101, 15, 1), Part2(102, 16, 2) },
            // Listening совпадает только для (15,1); для (16,2) нет — секция недоступна.
            listening: new[] { Listening(201, "ielts15-test1") },
            // Reading: book-привязанный (15,1) + сквозной acad-r (в матч по book не попадает).
            reading: new[] { Reading(301, "ielts15-r-test1"), Reading(302, "acad-r-test05") },
            // Writing к книгам не привязан — детерминированная ротация по индексу бандла.
            writing: new[] { Writing(401, "acad-w-test01"), Writing(402, "acad-w-test02") });

        var bundles = await picker.ListAsync();

        var b15 = bundles.Single(b => b is { Book: 15, TestNumber: 1 });
        var b16 = bundles.Single(b => b is { Book: 16, TestNumber: 2 });

        // (15,1): всё честно совпало → 4/4.
        Assert.Equal(201, b15.ListeningTestId);
        Assert.Equal(301, b15.ReadingTestSetId);          // book/test match
        Assert.Equal(401, b15.WritingTestSetId);          // rotation index 0
        Assert.Equal(4, b15.AvailableSections);

        // (16,2): Listening нет; Reading падает в ротацию (readingTests[1 % 2] = 302); Writing index 1.
        Assert.Null(b16.ListeningTestId);
        Assert.Equal(302, b16.ReadingTestSetId);          // rotation fallback
        Assert.Equal(402, b16.WritingTestSetId);          // rotation index 1
        Assert.Equal(3, b16.AvailableSections);           // S + R + W, без L
    }

    [Fact]
    public async Task ListAsync_matches_writing_by_book_test_from_title()
    {
        var picker = BuildPicker(
            p2: new[] { Part2(101, 15, 1), Part2(102, 16, 2) },
            listening: Array.Empty<ListeningTestSummary>(),
            reading: Array.Empty<ReadingTestSummary>(),
            // Намеренно в обратном порядке: правильный матч идёт по Title, а не по индексу-ротации.
            writing: new[] { WritingTagged(901, 16, 2), WritingTagged(902, 15, 1) });

        var bundles = await picker.ListAsync();

        var b15 = bundles.Single(b => b is { Book: 15, TestNumber: 1 });
        var b16 = bundles.Single(b => b is { Book: 16, TestNumber: 2 });

        // Если бы работала ротация, bundle #0 (15,1) получил бы writingSets[0]=901. Матч по Title → 902.
        Assert.Equal(902, b15.WritingTestSetId);
        Assert.Equal(901, b16.WritingTestSetId);
        Assert.Equal(2, b15.AvailableSections);   // только S + W (L/R пусты в этом тесте)
    }

    [Fact]
    public async Task ListAsync_part2_codes_that_do_not_match_regex_are_ignored()
    {
        var picker = BuildPicker(
            p2: new[]
            {
                Part2(101, 15, 1),
                new SpeakingTopicSummary(999, SpeakingPart.Part2, "freeform-topic-not-cambridge",
                    "Freeform", null, 1),
            },
            listening: Array.Empty<ListeningTestSummary>(),
            reading: Array.Empty<ReadingTestSummary>(),
            writing: Array.Empty<WritingTestSetSummary>());

        var bundles = await picker.ListAsync();

        var only = Assert.Single(bundles);
        Assert.Equal(101, only.SpeakingPart2BankId);
        Assert.Equal(1, only.AvailableSections);          // только Speaking-якорь
    }

    [Fact]
    public async Task ListAsync_empty_when_no_part2_anchors()
    {
        var picker = BuildPicker(
            p2: Array.Empty<SpeakingTopicSummary>(),
            listening: new[] { Listening(201, "ielts15-test1") },
            reading: new[] { Reading(301, "ielts15-r-test1") },
            writing: new[] { Writing(401, "acad-w-test01") });

        var bundles = await picker.ListAsync();

        Assert.Empty(bundles);
    }
}
