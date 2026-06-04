using System.Text.RegularExpressions;
using EnglishStudio.Modules.Ielts.Listening;
using EnglishStudio.Modules.Ielts.Reading;
using EnglishStudio.Modules.Ielts.Speaking;
using EnglishStudio.Modules.Ielts.Writing;

namespace EnglishStudio.Modules.Ielts.Mock;

/// <summary>
/// Собирает доступные Cambridge-бандлы для mock-экзамена.
/// Speaking Part2-банк — «якорь»: book/test парсятся из TopicCode "cambridge-{book}-test-{n}-part-2".
/// Listening и Reading матчатся по совпадению book/test ("ielts{book}-test{n}" и
/// "ielts{book}-r-test{n}" соответственно). Writing к book/test привязан через название теста
/// ("IELTS Test Book {book}, Test {test}" в Title/Attribution — коды acad-w- сквозные, но метаданные
/// несут книгу/тест), поэтому все 4 секции честно из одной Cambridge-книги. Любая секция без
/// book-совпадения падает в детерминированную ротацию по индексу бандла.
/// </summary>
public sealed partial class MockBundlePicker
{
    private readonly ISpeakingTestService _speaking;
    private readonly IListeningTestService _listening;
    private readonly IReadingTestService _reading;
    private readonly IWritingTaskService _writing;

    public MockBundlePicker(
        ISpeakingTestService speaking,
        IListeningTestService listening,
        IReadingTestService reading,
        IWritingTaskService writing)
    {
        _speaking = speaking;
        _listening = listening;
        _reading = reading;
        _writing = writing;
    }

    [GeneratedRegex(@"^cambridge-(\d+)-test-(\d+)-part-2$", RegexOptions.IgnoreCase)]
    private static partial Regex SpeakingPart2Regex();

    [GeneratedRegex(@"^ielts(\d+)-test(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ListeningRegex();

    [GeneratedRegex(@"^ielts(\d+)-r-test(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadingRegex();

    // Writing-наборы импортированы со сквозными кодами acad-w-, но Title/Attribution несут книгу и тест,
    // напр. "IELTS Test Book 15, Test 1" / "IELTS Test Book 15 Academic - Test 1".
    [GeneratedRegex(@"Book\s+(\d+)\D+?Test\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WritingTitleRegex();

    public async Task<IReadOnlyList<MockBundleSummary>> ListAsync(CancellationToken ct = default)
    {
        var part2Banks = await _speaking.ListTopicsAsync(SpeakingPart.Part2, ct);
        var listeningTests = await _listening.ListAsync(ct);
        var readingTests = await _reading.ListAsync(ct);
        var writingSets = await _writing.ListTestSetsAsync(ct);

        // Listening lookup по (book, test): "ielts{book}-test{n}".
        var listeningByKey = new Dictionary<(int Book, int Test), int>();
        foreach (var lt in listeningTests)
        {
            var m = ListeningRegex().Match(lt.Code);
            if (m.Success)
                listeningByKey[(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))] = lt.Id;
        }

        // Reading lookup по (book, test): "ielts{book}-r-test{n}" (Cambridge-импорт).
        // Сквозные acad-r-testNN сюда не попадают — они идут в ротацию-фолбэк.
        var readingByKey = new Dictionary<(int Book, int Test), int>();
        foreach (var rt in readingTests)
        {
            var m = ReadingRegex().Match(rt.Code);
            if (m.Success)
                readingByKey[(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))] = rt.Id;
        }

        // Writing lookup по (book, test): парсим из Title (фолбэк — Attribution).
        // "IELTS Test Book {book}, Test {test}". Несопоставимые наборы идут в ротацию-фолбэк.
        var writingByKey = new Dictionary<(int Book, int Test), int>();
        foreach (var ws in writingSets)
        {
            var m = WritingTitleRegex().Match(ws.Title);
            if (!m.Success && !string.IsNullOrEmpty(ws.Attribution))
                m = WritingTitleRegex().Match(ws.Attribution);
            if (m.Success)
                writingByKey[(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))] = ws.Id;
        }

        // Speaking-якоря, отсортированные по (book, test) для стабильной ротации R/W.
        var anchors = part2Banks
            .Select(b => (Bank: b, Match: SpeakingPart2Regex().Match(b.TopicCode)))
            .Where(x => x.Match.Success)
            .Select(x => (
                BankId: x.Bank.BankId,
                Book: int.Parse(x.Match.Groups[1].Value),
                Test: int.Parse(x.Match.Groups[2].Value)))
            .OrderBy(a => a.Book).ThenBy(a => a.Test)
            .ToList();

        var bundles = new List<MockBundleSummary>(anchors.Count);
        for (int i = 0; i < anchors.Count; i++)
        {
            var a = anchors[i];

            int? listeningId = listeningByKey.TryGetValue((a.Book, a.Test), out var lid) ? lid : null;

            // Reading: сначала честное совпадение по book/test, иначе ротация по всем reading-тестам.
            int? readingId = readingByKey.TryGetValue((a.Book, a.Test), out var rid)
                ? rid
                : readingTests.Count > 0 ? readingTests[i % readingTests.Count].Id : null;

            // Writing: сначала честное совпадение по book/test (из Title), иначе ротация по всем наборам.
            int? writingId = writingByKey.TryGetValue((a.Book, a.Test), out var wid)
                ? wid
                : writingSets.Count > 0 ? writingSets[i % writingSets.Count].Id : null;

            int available = 1 // Speaking — якорь, всегда есть
                + (listeningId.HasValue ? 1 : 0)
                + (readingId.HasValue ? 1 : 0)
                + (writingId.HasValue ? 1 : 0);

            bundles.Add(new MockBundleSummary(
                Book: a.Book,
                TestNumber: a.Test,
                ListeningTestId: listeningId,
                ReadingTestSetId: readingId,
                WritingTestSetId: writingId,
                SpeakingPart2BankId: a.BankId,
                AvailableSections: available));
        }

        return bundles;
    }

    /// <summary>Случайный bundle для «surprise me». Предпочитает бандлы с минимум R+W+S (3 секции).</summary>
    public async Task<MockBundleSummary?> PickRandomAsync(CancellationToken ct = default)
    {
        var all = await ListAsync(ct);
        if (all.Count == 0) return null;

        var usable = all.Where(b => b.AvailableSections >= 3).ToList();
        var pool = usable.Count > 0 ? usable : all;
        return pool[Random.Shared.Next(pool.Count)];
    }
}
