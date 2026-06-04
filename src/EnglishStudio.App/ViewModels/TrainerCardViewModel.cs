namespace EnglishStudio.App.ViewModels;

public enum TrainerCardKind
{
    Word,
    PhrasalVerb,
    Collocation,
}

/// <summary>A reading text's practice pool entry for the trainer's "повторить пул" list.</summary>
public sealed record TextPoolItem(int TextId, string Title, int Count)
{
    public string Label => $"{Title}  ·  {Count}";
}

public class TrainerCardViewModel
{
    public int ProgressId { get; init; }
    public TrainerCardKind Kind { get; init; }
    public int OwnerId { get; init; }  // WordId or PhrasalVerbId or CollocationId

    public string Headword { get; init; } = string.Empty;
    public string PartOfSpeechCode { get; init; } = string.Empty;
    public string PartOfSpeechNameRu { get; init; } = string.Empty;
    public string? IpaUk { get; init; }
    public string? IpaUs { get; init; }
    public bool HasAudioUk { get; init; }
    public bool HasAudioUs { get; init; }

    public IReadOnlyList<string> TranslationsRu { get; init; } = Array.Empty<string>();
    public string? DefinitionRu { get; init; }
    public string? DefinitionEn { get; init; }
    public IReadOnlyList<ExampleDetail> Examples { get; init; } = Array.Empty<ExampleDetail>();
}
