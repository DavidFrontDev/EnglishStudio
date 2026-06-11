using EnglishStudio.Modules.Dictionary.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Display-IPA resolver for the reader's per-word transcription. Tier 1 is the curated dictionary
/// (UK preferred, then US) — accurate, with stress; tier 2 is CMUdict via
/// <see cref="IPronunciationLexicon.GetDisplayIpa"/> (spelling-normalized + morphological fallback)
/// for everything the dictionary doesn't cover.
/// </summary>
public sealed class TranscriptionService : ITranscriptionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPronunciationLexicon _lexicon;
    private readonly ILogger<TranscriptionService> _log;

    public TranscriptionService(
        IServiceScopeFactory scopeFactory,
        IPronunciationLexicon lexicon,
        ILogger<TranscriptionService> log)
    {
        _scopeFactory = scopeFactory;
        _lexicon = lexicon;
        _log = log;
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> normalizedWords, CancellationToken ct = default)
    {
        var keys = normalizedWords
            .Where(w => !string.IsNullOrEmpty(w))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (keys.Count == 0) return result;

        var requested = new HashSet<string>(keys, StringComparer.Ordinal);

        // ── Tier 1: curated dictionary IPA (UK preferred), batched to respect SQLite limits. ──
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

            const int chunk = 400;
            for (var i = 0; i < keys.Count; i += chunk)
            {
                ct.ThrowIfCancellationRequested();
                var slice = keys.GetRange(i, Math.Min(chunk, keys.Count - i));

                var rows = await db.Words
                    .Where(w => slice.Contains(w.Headword) || slice.Contains(w.Lemma))
                    .Select(w => new { w.Headword, w.Lemma, w.IpaUk, w.IpaUs })
                    .ToListAsync(ct);

                foreach (var r in rows)
                {
                    var ipa = CleanIpa(!string.IsNullOrWhiteSpace(r.IpaUk) ? r.IpaUk : r.IpaUs);
                    if (ipa is null) continue;
                    // Headword wins over lemma where both map to the same requested key.
                    AddKey(result, requested, r.Headword, ipa, overwrite: true);
                    AddKey(result, requested, r.Lemma, ipa, overwrite: false);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Dictionary IPA batch lookup failed; falling back to CMUdict only.");
        }

        // ── Tier 2: CMUdict (+ spelling / morphology) for whatever the dictionary missed. ──
        foreach (var k in keys)
        {
            if (result.ContainsKey(k)) continue;
            var ipa = _lexicon.GetDisplayIpa(k);
            if (!string.IsNullOrEmpty(ipa)) result[k] = ipa!;
        }

        return result;
    }

    /// <summary>Adds the dictionary row under its normalized form, but only if it's a word we asked for.</summary>
    private static void AddKey(
        Dictionary<string, string> result, HashSet<string> requested, string raw, string ipa, bool overwrite)
    {
        var key = ReadingTokenizer.NormalizeWord(raw);
        if (key.Length == 0) return;
        if (!requested.Contains(key)) return;
        if (overwrite || !result.ContainsKey(key)) result[key] = ipa;
    }

    /// <summary>Strips surrounding /…/ or […] delimiters and keeps the first variant only.</summary>
    private static string? CleanIpa(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Trim('/', '[', ']').Trim();

        var sep = s.IndexOfAny(new[] { ',', ';', '/' });
        if (sep > 0) s = s[..sep].Trim();
        s = s.Trim('/', '[', ']').Trim();

        return s.Length == 0 ? null : s;
    }
}
