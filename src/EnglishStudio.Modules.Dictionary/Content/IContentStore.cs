using System.IO;

namespace EnglishStudio.Modules.Dictionary.Content;

/// <summary>
/// Доступ к импортированному контенту в %AppData%\EnglishStudio\IeltsContent.
/// Единый источник правды о том, какие секции присутствуют на диске.
/// </summary>
public interface IContentStore
{
    /// <summary>%AppData%\EnglishStudio\IeltsContent.</summary>
    string ContentRoot { get; }

    /// <summary>Корень модуля: ContentRoot/&lt;moduleFolder&gt; (напр. "Reading").</summary>
    string ModuleRoot(string moduleFolder);

    /// <summary>Импортирована ли секция (ключевой JSON / .txt на диске).</summary>
    bool IsImported(ContentSection section);

    /// <summary>Манифест из ContentRoot/manifest.json (null, если нет).</summary>
    ContentManifest? ReadManifest();

    /// <summary>Открыть JSON ContentRoot/&lt;moduleFolder&gt;/&lt;fileName&gt; или null, если файла нет.</summary>
    Stream? OpenJson(string moduleFolder, string fileName);

    /// <summary>Абсолютный путь ContentRoot/&lt;moduleFolder&gt;/&lt;code&gt;/&lt;relative&gt; (или null, если нет).</summary>
    string? ResolveFile(string moduleFolder, string code, string relative);
}
