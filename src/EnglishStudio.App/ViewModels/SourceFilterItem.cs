using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.App.ViewModels;

public class SourceFilterItem
{
    public WordSource? Source { get; init; }
    public string Label { get; init; } = string.Empty;

    public bool IsAll => Source is null;
}
