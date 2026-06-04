using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.App.ViewModels;

public class WordDetailViewModel
{
    public int Id { get; init; }
    public string Headword { get; init; } = string.Empty;
    public string PartOfSpeechNameRu { get; init; } = string.Empty;
    public string PartOfSpeechCode { get; init; } = string.Empty;
    public CefrLevel Cefr { get; init; }
    public string? IpaUk { get; init; }
    public string? IpaUs { get; init; }
    public bool HasAudioUk { get; init; }
    public bool HasAudioUs { get; init; }
    public bool IsInTraining { get; set; }
    public IReadOnlyList<SenseDetail> Senses { get; init; } = Array.Empty<SenseDetail>();
    public IReadOnlyList<ExampleDetail> Examples { get; init; } = Array.Empty<ExampleDetail>();
}

public class SenseDetail
{
    public string DefinitionEn { get; init; } = string.Empty;
    public string DefinitionRu { get; init; } = string.Empty;
    public IReadOnlyList<string> Translations { get; init; } = Array.Empty<string>();
}

public class ExampleDetail
{
    public string TextEn { get; init; } = string.Empty;
    public string? TextRu { get; init; }
    public string? Source { get; init; }
}
