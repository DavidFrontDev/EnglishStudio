using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

public sealed class TextLookupService : ITextLookupService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDictionaryEnrichmentService _enrichment;
    private readonly ILogger<TextLookupService> _log;

    public TextLookupService(
        IServiceScopeFactory scopeFactory,
        IDictionaryEnrichmentService enrichment,
        ILogger<TextLookupService> log)
    {
        _scopeFactory = scopeFactory;
        _enrichment = enrichment;
        _log = log;
    }

    public bool CanEnrich => _enrichment.IsAvailable;

    public async Task<WordLookupResult> LookupAsync(string selectedText, string? contextSentence = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
            return WordLookupResult.NotFound(string.Empty);

        var words = selectedText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var isPhrase = words.Length > 1;

        return isPhrase
            ? await LookupPhraseAsync(selectedText, contextSentence, ct)
            : await LookupSingleAsync(ReadingTokenizer.NormalizeWord(selectedText), contextSentence, ct);
    }

    private async Task<WordLookupResult> LookupSingleAsync(string norm, string? context, CancellationToken ct)
    {
        if (norm.Length == 0) return WordLookupResult.NotFound(norm);

        // 1) Dictionary.
        var found = await QueryWordAsync(norm, ct);
        if (found is not null) return found;

        // 2) Claude enrichment (persists), then re-read.
        if (_enrichment.IsAvailable)
        {
            var newId = await _enrichment.FetchAndPersistWordAsync(norm, context, ct);
            if (newId is int id)
            {
                var enriched = await QueryWordByIdAsync(id, ct);
                if (enriched is not null) return enriched;
            }
        }

        return WordLookupResult.NotFound(norm);
    }

    private async Task<WordLookupResult> LookupPhraseAsync(string phrase, string? context, CancellationToken ct)
    {
        var norm = NormalizePhrase(phrase);

        // Collocation / phrasal verb in the dictionary.
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

            var col = await db.Collocations
                .Where(c => c.LinkedText.ToLower() == norm && !string.IsNullOrWhiteSpace(c.TranslationRu))
                .Select(c => new { c.LinkedText, c.TranslationRu })
                .FirstOrDefaultAsync(ct);
            if (col is not null)
            {
                return new WordLookupResult
                {
                    Query = col.LinkedText,
                    Found = true,
                    IsPhrase = true,
                    TranslationsRu = new[] { col.TranslationRu! }
                };
            }
        }

        // Ephemeral AI translation (not persisted).
        if (_enrichment.IsAvailable)
        {
            var tr = await _enrichment.TranslatePhraseAsync(phrase.Trim(), context, ct);
            if (!string.IsNullOrWhiteSpace(tr))
            {
                return new WordLookupResult
                {
                    Query = phrase.Trim(),
                    Found = true,
                    IsPhrase = true,
                    IsAiGenerated = true,
                    TranslationsRu = new[] { tr! }
                };
            }
        }

        return WordLookupResult.NotFound(phrase.Trim(), isPhrase: true);
    }

    private async Task<WordLookupResult?> QueryWordAsync(string norm, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        // Prefer a match that actually carries senses/translations.
        var word = await db.Words
            .Include(w => w.PartOfSpeech)
            .Include(w => w.Senses).ThenInclude(s => s.Translations)
            .Where(w => w.Lemma == norm || w.Headword == norm)
            .OrderByDescending(w => w.Senses.Count)
            .FirstOrDefaultAsync(ct);

        return word is null ? null : MapWord(word);
    }

    private async Task<WordLookupResult?> QueryWordByIdAsync(int id, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        var word = await db.Words
            .Include(w => w.PartOfSpeech)
            .Include(w => w.Senses).ThenInclude(s => s.Translations)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        return word is null ? null : MapWord(word);
    }

    private static WordLookupResult MapWord(Word word)
    {
        var senses = word.Senses.OrderBy(s => s.OrderIndex).ToList();

        var translations = senses
            .SelectMany(s => s.Translations.OrderBy(t => t.OrderIndex).Select(t => t.TextRu))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        var defRu = senses.Select(s => s.DefinitionRu).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
        var defEn = senses.Select(s => s.DefinitionEn).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));

        return new WordLookupResult
        {
            Query = word.Headword,
            Found = true,
            Ipa = !string.IsNullOrWhiteSpace(word.IpaUk) ? word.IpaUk : word.IpaUs,
            PartOfSpeechRu = word.PartOfSpeech?.NameRu,
            TranslationsRu = translations,
            DefinitionRu = defRu,
            DefinitionEn = defEn,
            WordId = word.Id,
            IsAiGenerated = word.Source == WordSource.Ai || word.IsAiGenerated
        };
    }

    private static string NormalizePhrase(string phrase) =>
        string.Join(' ', phrase.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
