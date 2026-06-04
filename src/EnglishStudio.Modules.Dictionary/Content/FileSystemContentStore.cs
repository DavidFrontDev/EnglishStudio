using System.IO;
using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;

namespace EnglishStudio.Modules.Dictionary.Content;

/// <summary>Файловая реализация <see cref="IContentStore"/> поверх %AppData%\EnglishStudio\IeltsContent.</summary>
public sealed class FileSystemContentStore : IContentStore
{
    public string ContentRoot => DictionaryPaths.IeltsContentRoot;

    public string ModuleRoot(string moduleFolder) => Path.Combine(ContentRoot, moduleFolder);

    public bool IsImported(ContentSection section) => section switch
    {
        ContentSection.DictionaryOxford => File.Exists(Path.Combine(ContentRoot, "Dictionary", "oxford_5000.json")),
        ContentSection.DictionaryPhave  => File.Exists(Path.Combine(ContentRoot, "Dictionary", "phave.json")),
        ContentSection.Reading          => File.Exists(Path.Combine(ContentRoot, "Reading", "ielts_reading_tests.json")),
        ContentSection.Listening        => File.Exists(Path.Combine(ContentRoot, "Listening", "ielts_listening_tests.json")),
        ContentSection.Writing          => File.Exists(Path.Combine(ContentRoot, "Writing", "writing_tests.json")),
        ContentSection.Speaking         => Directory.Exists(Path.Combine(ContentRoot, "Speaking"))
                                           && Directory.EnumerateFiles(Path.Combine(ContentRoot, "Speaking"), "*.txt", SearchOption.AllDirectories).Any(),
        ContentSection.Rubrics          => File.Exists(Path.Combine(ContentRoot, "Rubrics", "IeltsRubric_Writing.md"))
                                           && File.Exists(Path.Combine(ContentRoot, "Rubrics", "IeltsRubric_Speaking.md")),
        _ => false,
    };

    public ContentManifest? ReadManifest()
    {
        var p = Path.Combine(ContentRoot, "manifest.json");
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<ContentManifest>(
            File.ReadAllText(p),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public Stream? OpenJson(string moduleFolder, string fileName)
    {
        var p = Path.Combine(ContentRoot, moduleFolder, fileName);
        return File.Exists(p) ? File.OpenRead(p) : null;
    }

    public string? ResolveFile(string moduleFolder, string code, string relative)
    {
        var p = Path.Combine(ContentRoot, moduleFolder, code, relative);
        return File.Exists(p) ? p : null;
    }
}
