namespace EnglishStudio.App.Shell;

public interface IModuleDescriptor
{
    string Code { get; }
    string NameRu { get; }
    string NameEn { get; }

    /// <summary>Display name in the current UI language; live-updates on language switch.</summary>
    string Name { get; }

    string IconGlyph { get; }
    int Order { get; }

    /// <summary>Which navigation area this module belongs to.</summary>
    AppArea Area { get; }

    object CreateView(IServiceProvider services);
}
