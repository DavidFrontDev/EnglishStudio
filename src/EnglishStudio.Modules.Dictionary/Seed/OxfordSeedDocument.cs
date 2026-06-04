using System.Text.Json.Serialization;

namespace EnglishStudio.Modules.Dictionary.Seed;

public sealed class OxfordSeedDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("sourceName")]
    public string SourceName { get; set; } = string.Empty;

    [JsonPropertyName("sourceRepo")]
    public string SourceRepo { get; set; } = string.Empty;

    [JsonPropertyName("audioBaseUrl")]
    public string AudioBaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("totalEntries")]
    public int TotalEntries { get; set; }

    [JsonPropertyName("words")]
    public List<OxfordSeedEntry> Words { get; set; } = new();
}

public sealed class OxfordSeedEntry
{
    [JsonPropertyName("headword")]
    public string Headword { get; set; } = string.Empty;

    [JsonPropertyName("pos")]
    public string Pos { get; set; } = string.Empty;

    [JsonPropertyName("posFull")]
    public string PosFull { get; set; } = string.Empty;

    [JsonPropertyName("cefr")]
    public string Cefr { get; set; } = string.Empty;

    [JsonPropertyName("ipaUk")]
    public string? IpaUk { get; set; }

    [JsonPropertyName("ipaUs")]
    public string? IpaUs { get; set; }

    [JsonPropertyName("definitionEn")]
    public string? DefinitionEn { get; set; }

    [JsonPropertyName("exampleEn")]
    public string? ExampleEn { get; set; }

    [JsonPropertyName("audioUk")]
    public string? AudioUk { get; set; }

    [JsonPropertyName("audioUs")]
    public string? AudioUs { get; set; }
}
