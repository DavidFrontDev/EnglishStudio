namespace EnglishStudio.Modules.Dictionary.Content;

/// <summary>Содержимое manifest.json в корне content-pack.</summary>
public sealed record ContentManifest
{
    public int PackVersion { get; init; } = 1;

    public string CreatedAt { get; init; } = "";

    /// <summary>Ключи: "dictionaryOxford","dictionaryPhave","reading","listening","writing","speaking".</summary>
    public Dictionary<string, bool> Sections { get; init; } = new();

    public bool Has(ContentSection s) => Sections.TryGetValue(KeyOf(s), out var v) && v;

    public static string KeyOf(ContentSection s) => s switch
    {
        ContentSection.DictionaryOxford => "dictionaryOxford",
        ContentSection.DictionaryPhave  => "dictionaryPhave",
        ContentSection.Reading          => "reading",
        ContentSection.Listening        => "listening",
        ContentSection.Writing          => "writing",
        ContentSection.Speaking         => "speaking",
        ContentSection.Rubrics          => "rubrics",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}
