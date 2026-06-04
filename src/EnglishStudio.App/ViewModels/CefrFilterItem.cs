using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.App.ViewModels;

public partial class CefrFilterItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public CefrLevel Level { get; init; }
    public string Label { get; init; } = string.Empty;
}
