using System.IO;
using System.Reflection;

namespace EnglishStudio.Modules.Dictionary.Seed;

public static class SeedManifest
{
    // Oxford 5000 and PHaVE are copyright content — they ship in a separate content-pack and are
    // loaded via IContentStore, not as embedded resources. AWL/AVL are CC0 academic word lists and
    // remain embedded here (see plans/Infra_Publish_GitHub_AgentExecution.md §A1.4, §A2).
    public const string AwlResourceName =
        "EnglishStudio.Modules.Dictionary.Seed.awl.json";

    public const string AvlResourceName =
        "EnglishStudio.Modules.Dictionary.Seed.avl.json";

    public static Stream OpenAwl() => Open(AwlResourceName);
    public static Stream OpenAvl() => Open(AvlResourceName);

    private static Stream Open(string name)
    {
        var asm = typeof(SeedManifest).Assembly;
        return asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource not found: {name}");
    }

    public static IReadOnlyList<string> ListEmbeddedResources()
    {
        return typeof(SeedManifest).Assembly.GetManifestResourceNames();
    }
}
