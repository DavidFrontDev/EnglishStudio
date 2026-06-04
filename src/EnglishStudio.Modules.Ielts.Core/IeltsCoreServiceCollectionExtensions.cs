using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Modules.Ielts.Core;

public static class IeltsCoreServiceCollectionExtensions
{
    public static IServiceCollection AddIeltsCoreModule(this IServiceCollection services)
    {
        services.AddDbContextFactory<IeltsDbContext>(opt =>
            opt.UseSqlite(
                DictionaryPaths.SqliteConnectionString,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory_Ielts")));

        services.AddSingleton<IBandScoreMapper, BandScoreMapper>();
        services.AddSingleton<IAnswerChecker, TextAnswerChecker>();
        services.AddSingleton<IAnswerChecker, ChoiceAnswerChecker>();
        services.AddSingleton<IAnswerChecker, MatchingAnswerChecker>();
        services.AddSingleton<AnswerCheckerRegistry>();
        services.AddSingleton<ITestRunner, TestRunner>();

        return services;
    }
}
