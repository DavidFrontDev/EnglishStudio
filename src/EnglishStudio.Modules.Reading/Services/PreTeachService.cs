using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Dictionary.Localization;
using EnglishStudio.Modules.Dictionary.Srs;
using EnglishStudio.Modules.Reading.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Pre-teach (F1): surfaces a text's harder/unfamiliar words and pushes them into SRS before
/// reading. Singleton; reads the dictionary through <see cref="IServiceScopeFactory"/> (the
/// context is scoped) exactly like <c>TextLibraryService</c>.
/// </summary>
public sealed class PreTeachService : IPreTeachService
{
    private const int MinWordLength = 3;
    private const int DictionaryQueryChunk = 400;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITextLibraryService _library;
    private readonly ISrsService _srs;
    private readonly IDictionaryEnrichmentService _enrichment;
    private readonly IMessageLocalizer _messages;
    private readonly ILogger<PreTeachService> _log;

    public PreTeachService(
        IServiceScopeFactory scopeFactory,
        ITextLibraryService library,
        ISrsService srs,
        IDictionaryEnrichmentService enrichment,
        IMessageLocalizer messages,
        ILogger<PreTeachService> log)
    {
        _scopeFactory = scopeFactory;
        _library = library;
        _srs = srs;
        _enrichment = enrichment;
        _messages = messages;
        _log = log;
    }

    public bool CanEnrich => _enrichment.IsAvailable;

    public async Task<PreTeachResult> AnalyzeAsync(int textId, PreTeachOptions? options = null, CancellationToken ct = default)
    {
        options ??= new PreTeachOptions();

        var detail = await _library.GetAsync(textId, ct);
        if (detail is null)
            return new PreTeachResult(textId, Array.Empty<PreTeachCandidate>(), 0, 0);

        // Distinct, normalized, non-trivial words with their occurrence counts.
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in ReadingTokenizer.Tokenize(detail.BodyText))
        {
            if (token.Kind != TokenKind.Word) continue;
            var norm = ReadingTokenizer.NormalizeWord(token.Text);
            if (norm.Length < MinWordLength) continue;
            if (!norm.Any(char.IsLetter)) continue;
            if (EnglishStopWords.IsStopWord(norm)) continue;
            occurrences[norm] = occurrences.TryGetValue(norm, out var c) ? c + 1 : 1;
        }

        var totalDistinct = occurrences.Count;
        if (totalDistinct == 0)
            return new PreTeachResult(textId, Array.Empty<PreTeachCandidate>(), 0, 0);

        var matches = await LookupDictionaryAsync(occurrences.Keys, ct);

        var knownCount = 0;
        var candidates = new List<PreTeachCandidate>();
        foreach (var (norm, count) in occurrences)
        {
            matches.TryGetValue(norm, out var m);
            var inDictionary = m is not null;
            var cefr = m?.Cefr ?? CefrLevel.Unknown;
            int? wordId = m?.WordId;

            var alreadyInTraining = wordId is int id && await _srs.IsInTrainingForWordAsync(id, ct);

            // "Known" = already being learned, or in-dictionary and below the pre-teach threshold.
            var known = alreadyInTraining
                        || (inDictionary && cefr != CefrLevel.Unknown && (int)cefr < (int)options.MinCefr);
            if (known) knownCount++;

            // Candidate rules (per plan): not already in SRS (when requested), AND either not in
            // the dictionary at all, or in the dictionary at/above the CEFR threshold.
            var passesTraining = !options.OnlyNotInTraining || !alreadyInTraining;
            var passesLevel = !inDictionary || (int)cefr >= (int)options.MinCefr;
            if (!passesTraining || !passesLevel) continue;

            candidates.Add(new PreTeachCandidate(
                Headword: m?.Headword ?? norm,
                Lemma: m?.Lemma ?? norm,
                TranslationRu: m?.Translation,
                Cefr: cefr,
                WordId: wordId,
                InDictionary: inDictionary,
                AlreadyInTraining: alreadyInTraining,
                Occurrences: count));
        }

        // Hardest first: unknown (not in dictionary) → higher CEFR → rarer → more frequent in text.
        var ordered = candidates
            .OrderBy(c => c.InDictionary)
            .ThenByDescending(c => (int)c.Cefr)
            .ThenByDescending(c => c.WordId is null ? int.MaxValue : 0) // tie-break unknowns up
            .ThenByDescending(c => c.Occurrences)
            .ThenBy(c => c.Headword, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, options.MaxWords))
            .ToList();

        return new PreTeachResult(textId, ordered, totalDistinct, knownCount);
    }

    public async Task<int> AddToTrainingAsync(
        IReadOnlyList<PreTeachCandidate> candidates,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (candidates is null || candidates.Count == 0) return 0;

        var added = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var wordId = candidate.WordId;
            if (wordId is null)
            {
                if (!_enrichment.IsAvailable)
                {
                    progress?.Report(_messages.Format("ReadStudy_PreTeachSkipNoTranslation", candidate.Headword));
                    continue;
                }

                progress?.Report(_messages.Format("ReadStudy_PreTeachTranslating", candidate.Headword));
                var lemma = string.IsNullOrWhiteSpace(candidate.Lemma) ? candidate.Headword : candidate.Lemma;
                wordId = await _enrichment.FetchAndPersistWordAsync(lemma, contextSentence: null, ct);
                if (wordId is null)
                {
                    _log.LogInformation("Pre-teach: could not enrich '{Word}'; skipping.", candidate.Headword);
                    continue;
                }
            }

            progress?.Report(_messages.Format("ReadStudy_PreTeachAdding", candidate.Headword));
            var prog = await _srs.AddWordAsync(wordId.Value, ct);
            if (prog is not null) added++;
        }

        progress?.Report(_messages.Format("ReadStudy_PreTeachDone", added));
        return added;
    }

    private async Task<Dictionary<string, DictMatch>> LookupDictionaryAsync(IEnumerable<string> distinctNorms, CancellationToken ct)
    {
        var map = new Dictionary<string, DictMatch>(StringComparer.Ordinal);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        foreach (var chunk in Chunk(distinctNorms, DictionaryQueryChunk))
        {
            var rows = await db.Words
                .Where(w => chunk.Contains(w.Lemma) || chunk.Contains(w.Headword))
                .Select(w => new
                {
                    w.Id,
                    w.Headword,
                    w.Lemma,
                    w.CefrLevel,
                    w.FrequencyRank,
                    Translation = w.Senses
                        .OrderBy(s => s.OrderIndex)
                        .SelectMany(s => s.Translations.OrderBy(t => t.OrderIndex).Select(t => t.TextRu))
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            foreach (var r in rows)
            {
                var match = new DictMatch(r.Id, r.Headword, r.Lemma, r.CefrLevel, r.FrequencyRank, r.Translation);
                Merge(map, ReadingTokenizer.NormalizeWord(r.Lemma), match);
                Merge(map, ReadingTokenizer.NormalizeWord(r.Headword), match);
            }
        }

        return map;
    }

    /// <summary>Keeps the most useful entry per word: prefers one with a translation, then the easier (lower) CEFR.</summary>
    private static void Merge(Dictionary<string, DictMatch> map, string key, DictMatch next)
    {
        if (key.Length == 0) return;
        if (!map.TryGetValue(key, out var cur)) { map[key] = next; return; }

        var translation = cur.Translation ?? next.Translation;
        var cefr = MinKnownCefr(cur.Cefr, next.Cefr);
        var freq = MinNullable(cur.FreqRank, next.FreqRank);
        // Prefer the id of whichever side carries a translation.
        var (id, headword, lemma) = cur.Translation is not null
            ? (cur.WordId, cur.Headword, cur.Lemma)
            : next.Translation is not null
                ? (next.WordId, next.Headword, next.Lemma)
                : (cur.WordId, cur.Headword, cur.Lemma);

        map[key] = new DictMatch(id, headword, lemma, cefr, freq, translation);
    }

    private static CefrLevel MinKnownCefr(CefrLevel a, CefrLevel b)
    {
        if (a == CefrLevel.Unknown) return b;
        if (b == CefrLevel.Unknown) return a;
        return (int)a <= (int)b ? a : b;
    }

    private static int? MinNullable(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

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

    private sealed record DictMatch(int WordId, string Headword, string Lemma, CefrLevel Cefr, int? FreqRank, string? Translation);
}
