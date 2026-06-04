namespace EnglishStudio.Modules.Dictionary.Content;

public sealed record ImportedSection(ContentSection Section, int ItemCount, bool Reseeded);

public sealed record ImportResult(
    bool Success,
    IReadOnlyList<ImportedSection> Sections,
    IReadOnlyList<string> Errors);
