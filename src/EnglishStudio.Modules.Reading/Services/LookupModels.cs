using System.Text.Json.Serialization;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>Result of a selection lookup, shown in the reader's translation popup.</summary>
public sealed record WordLookupResult
{
    public required string Query { get; init; }
    public bool Found { get; init; }

    public string? Ipa { get; init; }
    public string? PartOfSpeechRu { get; init; }
    public IReadOnlyList<string> TranslationsRu { get; init; } = Array.Empty<string>();
    public string? DefinitionRu { get; init; }
    public string? DefinitionEn { get; init; }

    /// <summary>Dictionary word id, when the result maps to a single word (enables "в изучение").</summary>
    public int? WordId { get; init; }

    public bool IsAiGenerated { get; init; }
    public bool IsPhrase { get; init; }

    public static WordLookupResult NotFound(string query, bool isPhrase = false) =>
        new() { Query = query, Found = false, IsPhrase = isPhrase };
}

/// <summary>Claude's JSON shape for an on-demand dictionary entry.</summary>
public sealed class AiWordEntry
{
    [JsonPropertyName("isRealWord")] public bool IsRealWord { get; set; }
    [JsonPropertyName("headword")] public string? Headword { get; set; }
    [JsonPropertyName("lemma")] public string? Lemma { get; set; }
    [JsonPropertyName("pos")] public string? Pos { get; set; }
    [JsonPropertyName("ipaUk")] public string? IpaUk { get; set; }
    [JsonPropertyName("ipaUs")] public string? IpaUs { get; set; }
    [JsonPropertyName("cefr")] public string? Cefr { get; set; }
    [JsonPropertyName("definitionEn")] public string? DefinitionEn { get; set; }
    [JsonPropertyName("definitionRu")] public string? DefinitionRu { get; set; }
    [JsonPropertyName("translationsRu")] public List<string>? TranslationsRu { get; set; }
    [JsonPropertyName("examples")] public List<AiExample>? Examples { get; set; }
}

public sealed class AiExample
{
    [JsonPropertyName("en")] public string? En { get; set; }
    [JsonPropertyName("ru")] public string? Ru { get; set; }
}
