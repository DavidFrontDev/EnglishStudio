using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.ViewModels;
using EnglishStudio.App.Views.Settings;

namespace EnglishStudio.App.Shell;

public partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, object> _viewCache = new();

    // Remembers the last module visited in each area so switching back restores it.
    private readonly Dictionary<AppArea, IModuleDescriptor> _lastModuleByArea = new();

    private readonly List<IModuleDescriptor> _studyModules;
    private readonly List<IModuleDescriptor> _ieltsModules;

    /// <summary>Global modules (e.g. Statistics) shown in the bottom zone, independent of the active area.</summary>
    public IReadOnlyList<IModuleDescriptor> GlobalModules { get; }

    /// <summary>Modules of the currently selected area — bound to the main sidebar list.</summary>
    public IReadOnlyList<IModuleDescriptor> VisibleModules =>
        CurrentArea == AppArea.Ielts ? _ieltsModules : _studyModules;

    [ObservableProperty]
    private AppArea _currentArea = AppArea.Study;

    [ObservableProperty]
    private object? _currentView;

    private IModuleDescriptor? _currentModule;

    public IModuleDescriptor? CurrentModule
    {
        get => _currentModule;
        set
        {
            // A ListBox whose ItemsSource doesn't contain the current selection pushes
            // null back through the TwoWay binding. Ignore those writes so switching
            // areas or selecting a global module doesn't clobber the active selection.
            if (value is null || ReferenceEquals(value, _currentModule)) return;

            SetProperty(ref _currentModule, value);

            if (value.Area is AppArea.Study or AppArea.Ielts)
                _lastModuleByArea[value.Area] = value;

            ShowView(value);
        }
    }

    public ShellViewModel(IEnumerable<IModuleDescriptor> modules, IServiceProvider services)
    {
        _services = services;

        var ordered = modules.OrderBy(m => m.Order).ToList();
        _studyModules = ordered.Where(m => m.Area == AppArea.Study).ToList();
        _ieltsModules = ordered.Where(m => m.Area == AppArea.Ielts).ToList();
        GlobalModules = ordered.Where(m => m.Area == AppArea.Global).ToList();

        CurrentModule = _studyModules.FirstOrDefault()
            ?? _ieltsModules.FirstOrDefault()
            ?? GlobalModules.FirstOrDefault();
    }

    partial void OnCurrentAreaChanged(AppArea value)
    {
        OnPropertyChanged(nameof(VisibleModules));

        // Entering an area restores its remembered module, or selects the first one.
        var target = _lastModuleByArea.TryGetValue(value, out var last)
            ? last
            : VisibleModules.FirstOrDefault();

        if (target is not null)
            CurrentModule = target;
    }

    private void ShowView(IModuleDescriptor module)
    {
        if (!_viewCache.TryGetValue(module.Code, out var view))
        {
            view = module.CreateView(_services);
            _viewCache[module.Code] = view;
        }

        CurrentView = view;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = _services.GetService(typeof(SettingsWindow)) as SettingsWindow;
        if (window is null) return;

        window.DataContext = _services.GetService(typeof(SettingsViewModel));
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();
    }
}
