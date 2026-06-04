using System.Text;
using System.Text.Json;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

public sealed class DictionaryEnrichmentService : IDictionaryEnrichmentService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Claude's free-form POS string → our seeded PartOfSpeech.Code.
    private static readonly Dictionary<string, string> PosCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["noun"] = "n", ["n"] = "n",
        ["verb"] = "v", ["v"] = "v",
        ["adjective"] = "adj", ["adj"] = "adj",
        ["adverb"] = "adv", ["adv"] = "adv",
        ["pronoun"] = "pron", ["pron"] = "pron",
        ["preposition"] = "prep", ["prep"] = "prep",
        ["determiner"] = "det", ["det"] = "det",
        ["number"] = "num", ["numeral"] = "num",
        ["conjunction"] = "conj", ["conj"] = "conj",
        ["exclamation"] = "exclam", ["interjection"] = "exclam",
        ["modal verb"] = "modal", ["modal"] = "modal",
        ["auxiliary verb"] = "aux",
        ["phrasal verb"] = "phrasal_verb",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClaudeCliClient _cli;
    private readonly ILogger<DictionaryEnrichmentService> _log;

    public DictionaryEnrichmentService(
        IServiceScopeFactory scopeFactory,
        IClaudeCliClient cli,
        ILogger<DictionaryEnrichmentService> log)
    {
        _scopeFactory = scopeFactory;
        _cli = cli;
        _log = log;
    }

    public bool IsAvailable => _cli.IsAvailable;

    public async Task<int?> FetchAndPersistWordAsync(string lemma, string? contextSentence, CancellationToken ct = default)
    {
        if (!_cli.IsAvailable || string.IsNullOrWhiteSpace(lemma)) return null;

        var prompt = BuildWordPrompt(lemma, contextSentence);

        ClaudeCliResponse response;
        try
        {
            response = await _cli.RunAsync(prompt, ClaudeOutputFormat.Json, timeout: TimeSpan.FromSeconds(90), ct: ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Claude enrichment call failed for '{Lemma}'", lemma);
            return null;
        }

        if (response.IsError || string.IsNullOrWhiteSpace(response.Text)) return null;

        var entry = TryParse<AiWordEntry>(response.Text);
        if (entry is null || !entry.IsRealWord) return null;

        var headword = (entry.Headword ?? lemma).Trim();
        if (headword.Length == 0) return null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

            var posCode = ResolvePosCode(entry.Pos);
            var pos = await db.PartsOfSpeech.FirstOrDefaultAsync(p => p.Code == posCode, ct)
                      ?? await db.PartsOfSpeech.FirstOrDefaultAsync(p => p.Code == "other", ct);
            if (pos is null) return null;

            // Idempotency: respect the unique (Headword, PartOfSpeechId) index.
            var existing = await db.Words
                .FirstOrDefaultAsync(w => w.Headword == headword && w.PartOfSpeechId == pos.Id, ct);
            if (existing is not null) return existing.Id;

            var now = DateTime.UtcNow;
            var word = new Word
            {
                Headword = headword,
                Lemma = (entry.Lemma ?? headword).Trim().ToLowerInvariant(),
                IpaUk = Clean(entry.IpaUk),
                IpaUs = Clean(entry.IpaUs),
                CefrLevel = ParseCefr(entry.Cefr),
                Source = WordSource.Ai,
                IsAiGenerated = true,
                PartOfSpeechId = pos.Id,
                CreatedAt = now,
                UpdatedAt = now
            };

            var sense = new Sense
            {
                DefinitionEn = Clean(entry.DefinitionEn) ?? string.Empty,
                DefinitionRu = Clean(entry.DefinitionRu) ?? string.Empty,
                OrderIndex = 0
            };

            if (entry.TranslationsRu is { Count: > 0 })
            {
                var order = 0;
                foreach (var tr in entry.TranslationsRu.Where(t => !string.IsNullOrWhiteSpace(t)))
                    sense.Translations.Add(new Translation { TextRu = tr.Trim(), OrderIndex = order++ });
            }

            if (entry.Examples is { Count: > 0 })
            {
                foreach (var ex in entry.Examples.Where(e => !string.IsNullOrWhiteSpace(e.En)))
                    sense.Examples.Add(new Example { TextEn = ex.En!.Trim(), TextRu = Clean(ex.Ru), Source = "ai" });
            }

            word.Senses.Add(sense);
            db.Words.Add(word);
            await db.SaveChangesAsync(ct);

            _log.LogInformation("AI-enriched dictionary with '{Headword}' ({Pos}), id={Id}", headword, posCode, word.Id);
            return word.Id;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to persist AI word '{Lemma}'", lemma);
            return null;
        }
    }

    public async Task<string?> TranslatePhraseAsync(string phrase, string? contextSentence, CancellationToken ct = default)
    {
        if (!_cli.IsAvailable || string.IsNullOrWhiteSpace(phrase)) return null;

        var sb = new StringBuilder();
        sb.AppendLine("You are a bilingual EN→RU dictionary. Translate the English phrase below into");
        sb.AppendLine("natural Russian. Respond with ONLY a JSON object: {\"translationRu\": \"...\"}.");
        sb.AppendLine("No prose, no markdown fence.");
        if (!string.IsNullOrWhiteSpace(contextSentence))
        {
            sb.AppendLine();
            sb.AppendLine($"Sentence context: {contextSentence.Trim()}");
        }
        sb.AppendLine();
        sb.AppendLine($"Phrase: {phrase.Trim()}");
        sb.AppendLine("Return JSON now.");

        try
        {
            var response = await _cli.RunAsync(sb.ToString(), ClaudeOutputFormat.Json, timeout: TimeSpan.FromSeconds(60), ct: ct);
            if (response.IsError || string.IsNullOrWhiteSpace(response.Text)) return null;

            using var doc = JsonDocument.Parse(ExtractJson(response.Text) ?? "{}");
            if (doc.RootElement.TryGetProperty("translationRu", out var tr))
                return Clean(tr.GetString());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Phrase translation failed for '{Phrase}'", phrase);
        }
        return null;
    }

    private static string BuildWordPrompt(string lemma, string? contextSentence)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a precise English→Russian learner's dictionary (Oxford-style, IELTS-grade).");
        sb.AppendLine("Produce a dictionary entry for the requested English word. Respond with ONLY a single");
        sb.AppendLine("JSON object matching this exact schema — no prose, no markdown fence, no comments:");
        sb.AppendLine("""
        {
          "isRealWord": true,
          "headword": "string (canonical spelling)",
          "lemma": "string (lowercase base form)",
          "pos": "noun|verb|adjective|adverb|pronoun|preposition|determiner|conjunction|number|exclamation|modal verb|phrasal verb|other",
          "ipaUk": "string or null (IPA, no slashes)",
          "ipaUs": "string or null",
          "cefr": "A1|A2|B1|B2|C1|C2|Unknown",
          "definitionEn": "short English definition",
          "definitionRu": "краткое определение по-русски",
          "translationsRu": ["перевод1", "перевод2"],
          "examples": [{"en": "example sentence", "ru": "перевод примера"}]
        }
        """);
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- If the input is a proper noun, a typo, or not a real English word, set isRealWord=false and leave other fields empty.");
        sb.AppendLine("- Choose the part of speech and sense that best fit the sentence context, if given.");
        sb.AppendLine("- 1–4 Russian translations, 1–2 examples. Keep it concise.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(contextSentence))
            sb.AppendLine($"Sentence where the word appears: {contextSentence.Trim()}");
        sb.AppendLine($"Word: {lemma.Trim()}");
        sb.AppendLine("Return JSON now.");
        return sb.ToString();
    }

    private T? TryParse<T>(string raw) where T : class
    {
        var json = ExtractJson(raw);
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse Claude JSON: {Raw}", raw[..Math.Min(raw.Length, 300)]);
            return null;
        }
    }

    private static string? ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return (start >= 0 && end > start) ? raw[start..(end + 1)] : null;
    }

    private static string ResolvePosCode(string? pos)
    {
        if (string.IsNullOrWhiteSpace(pos)) return "other";
        return PosCodeMap.TryGetValue(pos.Trim(), out var code) ? code : "other";
    }

    private static CefrLevel ParseCefr(string? cefr) =>
        Enum.TryParse<CefrLevel>(cefr?.Trim(), ignoreCase: true, out var lvl) ? lvl : CefrLevel.Unknown;

    private static string? Clean(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim().Trim('/');
}
