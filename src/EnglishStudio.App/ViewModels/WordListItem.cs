using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.App.ViewModels;

public class WordListItem
{
    public int Id { get; init; }
    public string Headword { get; init; } = string.Empty;
    public string? IpaUk { get; init; }
    public CefrLevel Cefr { get; init; }
    public string PosCode { get; init; } = string.Empty;
}
