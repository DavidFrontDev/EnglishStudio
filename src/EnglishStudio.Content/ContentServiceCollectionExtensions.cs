using EnglishStudio.Modules.Dictionary.Content;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Content;

public static class ContentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the content-pack import orchestrator. The <see cref="IContentStore"/> it depends on
    /// is registered by AddDictionaryModule (see plan §2.4); call this after that on the module chain.
    /// </summary>
    public static IServiceCollection AddContentModule(this IServiceCollection services)
    {
        services.AddSingleton<IContentImportService, ContentImportService>();
        return services;
    }
}
