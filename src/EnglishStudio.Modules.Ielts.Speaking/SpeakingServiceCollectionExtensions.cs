using System.IO;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Speaking.Cambridge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ielts.Speaking;

public static class SpeakingServiceCollectionExtensions
{
    public static IServiceCollection AddIeltsSpeakingModule(this IServiceCollection services)
    {
        services.AddSingleton<SpeechMetricsAnalyzer>();
        services.AddSingleton<ISpeakingTestService, SpeakingTestService>();
        services.AddSingleton<ISpeakingFeedbackService, SpeakingFeedbackService>();
        services.AddSingleton<CambridgeSpeakingTestParser>();

        // Speaking content lives in the imported content-pack at IeltsContent/Speaking
        // (layout: Speaking/Ielts {book}/Test№{t}.txt). The importer soft-skips if absent.
        services.AddSingleton(sp => new CambridgeSpeakingImportService(
            sp.GetRequiredService<IDbContextFactory<IeltsDbContext>>(),
            sp.GetRequiredService<CambridgeSpeakingTestParser>(),
            sp.GetRequiredService<ILogger<CambridgeSpeakingImportService>>(),
            baseFolder: Path.Combine(DictionaryPaths.IeltsContentRoot, "Speaking")));
        return services;
    }
}
