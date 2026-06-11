using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Localization;
using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnglishStudio.Modules.Reading;

public static class ReadingServiceCollectionExtensions
{
    /// <summary>Registers the study "Чтение" module (Учёба area).</summary>
    public static IServiceCollection AddReadingStudyModule(this IServiceCollection services)
    {
        services.AddDbContextFactory<ReadingDbContext>(opt =>
            opt.UseSqlite(
                DictionaryPaths.SqliteConnectionString,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory_Reading")));

        // Хост (App) перекрывает это реальным локализатором; в тестах/тулинге работает key-echo.
        services.TryAddSingleton<IMessageLocalizer, KeyEchoMessageLocalizer>();

        services.AddSingleton<ITextLibraryService, TextLibraryService>();
        services.AddSingleton<IDictionaryEnrichmentService, DictionaryEnrichmentService>();
        services.AddSingleton<ITextLookupService, TextLookupService>();
        services.AddSingleton<IReadingSessionService, ReadingSessionService>();
        services.AddSingleton<IPreTeachService, PreTeachService>();
        services.AddSingleton<IComprehensionService, ComprehensionService>();
        services.AddSingleton<IPronunciationLexicon, PronunciationLexicon>();
        services.AddSingleton<ITranscriptionService, TranscriptionService>();
        services.AddSingleton<IPhonemeFeedbackService, PhonemeFeedbackService>();
        services.AddSingleton<SentenceSplitter>();
        services.AddSingleton<IPaginationService, TextPaginator>();
        services.AddSingleton<INotesService, NotesService>();
        services.AddSingleton<IReadingStatsService, ReadingStatsService>();
        services.AddSingleton<IReadingPracticeService, ReadingPracticeService>();
        services.AddSingleton<Seed.GradedReaderSeedService>();

        return services;
    }
}
