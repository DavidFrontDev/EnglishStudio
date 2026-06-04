namespace EnglishStudio.App.ViewModels;

public class PosFilterItem
{
    public string? Code { get; init; }
    public string Label { get; init; } = string.Empty;

    public bool IsAll => Code is null;
}
