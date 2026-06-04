namespace EnglishStudio.App.Shell;

public sealed class ModuleDescriptor : IModuleDescriptor
{
    private readonly Func<IServiceProvider, object> _viewFactory;

    public ModuleDescriptor(
        string code,
        string nameRu,
        string iconGlyph,
        int order,
        Func<IServiceProvider, object> viewFactory,
        AppArea area = AppArea.Study)
    {
        Code = code;
        NameRu = nameRu;
        IconGlyph = iconGlyph;
        Order = order;
        _viewFactory = viewFactory;
        Area = area;
    }

    public string Code { get; }
    public string NameRu { get; }
    public string IconGlyph { get; }
    public int Order { get; }
    public AppArea Area { get; }

    public object CreateView(IServiceProvider services) => _viewFactory(services);
}
