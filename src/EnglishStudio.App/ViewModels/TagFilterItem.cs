namespace EnglishStudio.App.ViewModels;

public class TagFilterItem
{
    public int? Id { get; init; }
    public string? Code { get; init; }
    public string Label { get; init; } = string.Empty;

    public bool IsAll => Id is null;
}
