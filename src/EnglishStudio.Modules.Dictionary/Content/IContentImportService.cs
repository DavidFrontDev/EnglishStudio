using System.Threading;
using System.Threading.Tasks;

namespace EnglishStudio.Modules.Dictionary.Content;

/// <summary>
/// Импорт content-pack (папка или .zip) в ContentRoot + (ре)сидинг затронутых модулей.
/// Контракт живёт в Modules.Dictionary; реализация (ContentImportService) — в проекте
/// EnglishStudio.Content (см. plans/Infra_Publish_GitHub_AgentExecution.md §1.2, §A5).
/// </summary>
public interface IContentImportService
{
    /// <summary>Прочитать manifest из папки ИЛИ .zip без импорта (для валидации/превью).</summary>
    ContentManifest? PeekManifest(string packPathFolderOrZip);

    /// <summary>Импортировать pack (папка или .zip) в ContentRoot и пере-сидить затронутые модули.</summary>
    Task<ImportResult> ImportAsync(
        string packPathFolderOrZip,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
