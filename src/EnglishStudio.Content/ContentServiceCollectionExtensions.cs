using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnglishStudio.Content;

public static class ContentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the content-pack import orchestrator. The <see cref="IContentStore"/> it depends on
    /// is registered by AddDictionaryModule (see plan §2.4); call this after that on the module chain.
    /// </summary>
    public static IServiceCollection AddContentModule(this IServiceCollection services)
    {
        // Хост (App) перекрывает это реальным локализатором; в тестах/тулинге работает key-echo.
        services.TryAddSingleton<IMessageLocalizer, KeyEchoMessageLocalizer>();
        services.AddSingleton<IContentImportService, ContentImportService>();
        return services;
    }
}
