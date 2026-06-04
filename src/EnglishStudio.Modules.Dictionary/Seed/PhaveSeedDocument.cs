using System.Text.Json.Serialization;

namespace EnglishStudio.Modules.Dictionary.Seed;

public sealed class PhaveSeedDocument
{
    [JsonPropertyName("SchemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("SourceName")]
    public string SourceName { get; set; } = string.Empty;

    [JsonPropertyName("SourceUrl")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("GeneratedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("Entries")]
    public List<PhaveSeedEntry> Entries { get; set; } = new();
}

public sealed class PhaveSeedEntry
{
    [JsonPropertyName("Rank")]
    public int Rank { get; set; }

    [JsonPropertyName("Phrase")]
    public string Phrase { get; set; } = string.Empty;

    [JsonPropertyName("Senses")]
    public List<PhaveSeedSense> Senses { get; set; } = new();
}

public sealed class PhaveSeedSense
{
    [JsonPropertyName("Num")]
    public int Num { get; set; }

    [JsonPropertyName("Particle")]
    public string? Particle { get; set; }

    [JsonPropertyName("DefinitionEn")]
    public string DefinitionEn { get; set; } = string.Empty;

    [JsonPropertyName("PercentOccurrence")]
    public double PercentOccurrence { get; set; }

    [JsonPropertyName("ExampleEn")]
    public string ExampleEn { get; set; } = string.Empty;
}
