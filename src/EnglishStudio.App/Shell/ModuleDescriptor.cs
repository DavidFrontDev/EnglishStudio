using System.ComponentModel;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Dictionary.Images;

namespace EnglishStudio.App.Shell;

public sealed class ModuleDescriptor : IModuleDescriptor, INotifyPropertyChanged
{
    private readonly Func<IServiceProvider, object> _viewFactory;

    public ModuleDescriptor(
        string code,
        string nameRu,
        string nameEn,
        string iconGlyph,
        int order,
        Func<IServiceProvider, object> viewFactory,
        AppArea area = AppArea.Study)
    {
        Code = code;
        NameRu = nameRu;
        NameEn = nameEn;
        IconGlyph = iconGlyph;
        Order = order;
        _viewFactory = viewFactory;
        Area = area;

        // Live-update the sidebar label when the interface language changes.
        LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public string Code { get; }
    public string NameRu { get; }
    public string NameEn { get; }
    public string Name => LocalizationManager.Instance.Current == AppLanguage.En ? NameEn : NameRu;
    public string IconGlyph { get; }
    public int Order { get; }
    public AppArea Area { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));

    public object CreateView(IServiceProvider services) => _viewFactory(services);
}
