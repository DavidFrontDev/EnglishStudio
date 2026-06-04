namespace EnglishStudio.App.Shell;

public interface IModuleDescriptor
{
    string Code { get; }
    string NameRu { get; }
    string IconGlyph { get; }
    int Order { get; }

    /// <summary>Which navigation area this module belongs to.</summary>
    AppArea Area { get; }

    object CreateView(IServiceProvider services);
}
