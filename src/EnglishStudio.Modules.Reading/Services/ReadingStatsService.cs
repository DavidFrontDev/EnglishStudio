using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Reading progress analytics (F6): speed/accuracy trend from completed <c>ReadingSession</c>s and
/// per-text vocabulary coverage (distinct content words known via SRS or an elementary CEFR).
/// Singleton; reading data via <see cref="IDbContextFactory{ReadingDbContext}"/>, dictionary/SRS
/// via <see cref="IServiceScopeFactory"/> (scoped context) like the other services.
/// </summary>
public sealed class ReadingStatsService : IReadingStatsService
{
    private const int MinWordLength = 3;
    private const int DictionaryQueryChunk = 400;
    // Words at or below this CEFR are assumed already known to a reader.
    private const CefrLevel KnownCefrCeiling = CefrLevel.A2;

    private readonly IDbContextFactory<ReadingDbContext> _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReadingStatsService> _log;

    public ReadingStatsService(
        IDbContextFactory<ReadingDbContext> factory,
        IServiceScopeFactory scopeFactory,
        ILogger<ReadingStatsService> log)
    {
        _factory = factory;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public async Task<ReadingStatsSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var sessions = await db.ReadingSessions
            .Where(s => s.Completed)
            .OrderBy(s => s.StartedAt)
            .Select(s => new { s.StartedAt, s.Wpm, s.AccuracyPct, s.WordsRead, s.DurationSec })
            .ToListAsync(ct);

        if (sessions.Count == 0)
            return new ReadingStatsSummary(0, 0, 0, 0, 0, Array.Empty<ReadingSpeedPoint>());

        var withWpm = sessions.Where(s => s.Wpm > 0).ToList();
        var trend = sessions
            .Select(s => new ReadingSpeedPoint(s.StartedAt, Math.Round(s.Wpm, 1), Math.Round(s.AccuracyPct, 1)))
            .ToList();

        return new ReadingStatsSummary(
            SessionsTotal: sessions.Count,
            WordsReadTotal: sessions.Sum(s => s.WordsRead),
            AvgWpm: withWpm.Count > 0 ? Math.Round(withWpm.Average(s => s.Wpm), 1) : 0,
            BestWpm: withWpm.Count > 0 ? Math.Round(withWpm.Max(s => s.Wpm), 1) : 0,
            MinutesReadTotal: (int)Math.Round(sessions.Sum(s => s.DurationSec) / 60.0),
            SpeedTrend: trend);
    }

    public async Task<IReadOnlyList<ReadingSpeedPoint>> GetSpeedTrendAsync(int? textId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.ReadingSessions.Where(s => s.Completed);
        if (textId is int id) query = query.Where(s => s.ReadingTextId == id);

        return await query
            .OrderBy(s => s.StartedAt)
            .Select(s => new ReadingSpeedPoint(s.StartedAt, Math.Round(s.Wpm, 1), Math.Round(s.AccuracyPct, 1)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VocabCoverage>> GetCoverageAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var texts = await db.ReadingTexts
            .Select(t => new { t.Id, t.Title, t.BodyText })
            .ToListAsync(ct);
        if (texts.Count == 0) return Array.Empty<VocabCoverage>();

        using var scope = _scopeFactory.CreateScope();
        var dict = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        var knownWordIds = (await dict.UserWordProgress
            .Where(p => p.WordId != null)
            .Select(p => p.WordId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var result = new List<VocabCoverage>(texts.Count);
        foreach (var text in texts)
        {
            var distinct = DistinctContentWords(text.BodyText);
            if (distinct.Count == 0)
            {
                result.Add(new VocabCoverage(text.Id, text.Title, 0, 0, 0));
                continue;
            }

            var knownNorms = await ComputeKnownNormsAsync(dict, distinct, knownWordIds, ct);
            var known = knownNorms.Count;
            var coverage = Math.Round(100.0 * known / distinct.Count, 1);
            result.Add(new VocabCoverage(text.Id, text.Title, distinct.Count, known, coverage));
        }

        return result.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static HashSet<string> DistinctContentWords(string body)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in ReadingTokenizer.Tokenize(body))
        {
            if (token.Kind != TokenKind.Word) continue;
            var norm = ReadingTokenizer.NormalizeWord(token.Text);
            if (norm.Length < MinWordLength) continue;
            if (!norm.Any(char.IsLetter)) continue;
            if (EnglishStopWords.IsStopWord(norm)) continue;
            set.Add(norm);
        }
        return set;
    }

    /// <summary>
    /// Returns the subset of <paramref name="distinct"/> considered "known": any dictionary entry
    /// for the word is in SRS, or carries a CEFR at/below the elementary ceiling.
    /// </summary>
    private static async Task<HashSet<string>> ComputeKnownNormsAsync(
        DictionaryDbContext dict, HashSet<string> distinct, HashSet<int> knownWordIds, CancellationToken ct)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in Chunk(distinct, DictionaryQueryChunk))
        {
            var rows = await dict.Words
                .Where(w => chunk.Contains(w.Lemma) || chunk.Contains(w.Headword))
                .Select(w => new { w.Id, w.Lemma, w.Headword, w.CefrLevel })
                .ToListAsync(ct);

            foreach (var r in rows)
            {
                var isKnown = knownWordIds.Contains(r.Id)
                              || (r.CefrLevel != CefrLevel.Unknown && (int)r.CefrLevel <= (int)KnownCefrCeiling);
                if (!isKnown) continue;

                foreach (var norm in new[] { ReadingTokenizer.NormalizeWord(r.Lemma), ReadingTokenizer.NormalizeWord(r.Headword) })
                    if (distinct.Contains(norm)) known.Add(norm);
            }
        }

        return known;
    }

    private static IEnumerable<List<string>> Chunk(IEnumerable<string> source, int size)
    {
        var bucket = new List<string>(size);
        foreach (var item in source)
        {
            bucket.Add(item);
            if (bucket.Count == size) { yield return bucket; bucket = new List<string>(size); }
        }
        if (bucket.Count > 0) yield return bucket;
    }
}
