using System.Windows;
using EnglishStudio.App.Audio;
using EnglishStudio.App.Configuration;
using EnglishStudio.App.Content;
using EnglishStudio.App.Diagnostics;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Reading;
using EnglishStudio.App.ViewModels.Content;
using EnglishStudio.App.Views.Content;
using EnglishStudio.App.Shell;
using EnglishStudio.App.Theming;
using EnglishStudio.App.ViewModels;
using EnglishStudio.App.ViewModels.Listening;
using EnglishStudio.App.ViewModels.Mock;
using EnglishStudio.App.ViewModels.Reading;
using EnglishStudio.App.ViewModels.ReadingStudy;
using EnglishStudio.App.ViewModels.Speaking;
using EnglishStudio.App.ViewModels.Writing;
using EnglishStudio.App.Views.Dictionary;
using EnglishStudio.App.Views.Listening;
using EnglishStudio.App.Views.Mock;
using EnglishStudio.App.Views.Reading;
using EnglishStudio.App.Views.ReadingStudy;
using EnglishStudio.App.Views.Speaking;
using EnglishStudio.App.Views.Stats;
using EnglishStudio.App.Views.Trainer;
using EnglishStudio.App.Views.Writing;
using EnglishStudio.Content;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Images;
using EnglishStudio.Modules.Dictionary.Seed;
using EnglishStudio.Modules.Ielts.Core;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Listening;
using EnglishStudio.Modules.Ielts.Listening.Seed;
using EnglishStudio.Modules.Ielts.Mock;
using EnglishStudio.Modules.Ielts.Reading;
using EnglishStudio.Modules.Ielts.Reading.Seed;
using EnglishStudio.Modules.Ielts.Speaking;
using EnglishStudio.Modules.Ielts.Speaking.Cambridge;
using EnglishStudio.Modules.Ielts.Writing;
using EnglishStudio.Modules.Ielts.Writing.Seed;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Velopack;

namespace EnglishStudio.App;

public partial class App : Application
{
    private IHost? _host;
    private DateTime _lastCrashDialogUtc;

    public App()
    {
        // Must run before any other startup so Velopack can service install/update/uninstall hook
        // invocations and exit, before the WPF UI initialises. Harmless (returns) on a normal launch.
        VelopackApp.Build().Run();
        RegisterGlobalExceptionHandlers();
    }

    /// <summary>
    /// Last-resort safety net: any exception that escapes a command/event handler used to kill the
    /// whole process. Dispatcher exceptions are logged and the app keeps running; unobserved task
    /// faults are logged and marked observed; AppDomain-level failures (process is already dying)
    /// get best-effort logging only.
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LogUnhandled("Dispatcher", e.Exception, fatal: false);
            e.Handled = true;
            ShowCrashDialogThrottled(e.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnhandled("UnobservedTask", e.Exception, fatal: false);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogUnhandled("AppDomain", e.ExceptionObject as Exception, fatal: true);
    }

    private void LogUnhandled(string source, Exception? ex, bool fatal)
    {
        if (ex is null) return;
        try
        {
            var logger = _host?.Services.GetService<ILogger<App>>();
            logger?.LogError(ex, "Unhandled exception ({Source})", source);
        }
        catch
        {
            // The safety net itself must never throw.
        }
        CrashReporter.Write(source, ex, fatal);
    }

    private void ShowCrashDialogThrottled(Exception ex)
    {
        // A fault inside a render/layout loop can re-throw every frame — one dialog per 10 s is enough.
        var now = DateTime.UtcNow;
        if (now - _lastCrashDialogUtc < TimeSpan.FromSeconds(10)) return;
        _lastCrashDialogUtc = now;

        try
        {
            MessageBox.Show(
                Loc.Format("App_UnexpectedErrorBody", ex.Message),
                Loc.Tr("App_UnexpectedErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // The safety net itself must never throw.
        }
    }

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host is not initialised.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDictionaryModule()
                        .AddAiModule()
                        .AddIeltsCoreModule()
                        .AddIeltsReadingModule()
                        .AddIeltsListeningModule()
                        .AddIeltsWritingModule()
                        .AddIeltsSpeakingModule()
                        .AddIeltsMockModule()
                        .AddReadingStudyModule()
                        .AddReadingEngine();

                // ── content-infra services (Agent A) ──
                // Content-pack import orchestrator (IContentImportService). IContentStore itself
                // comes from AddDictionaryModule above.
                services.AddContentModule();
                // ── content-infra services END ──

                services.AddSingleton<IPostConfigureOptions<ClaudeCliOptions>, ClaudeCliOptionsFromAppSettings>();

                // UI localization (RU/EN). Shared singleton so the {loc:Tr} markup extension and DI
                // consumers resolve the same instance — a language switch updates every binding.
                services.AddSingleton<ILocalizer>(_ => LocalizationManager.Instance);
                // Overrides the modules' key-echo default so their messages use the resx strings.
                services.AddSingleton<Modules.Dictionary.Localization.IMessageLocalizer>(
                    _ => new ModuleMessageLocalizer(LocalizationManager.Instance));

                services.AddSingleton<IThemeManager>(_ => new ThemeManager(Current));
                services.AddSingleton<IAudioPlayer, NAudioPlayer>();
                services.AddTransient<IListeningAudioPlayer, ListeningAudioPlayer>();
                services.AddSingleton<IAudioRecorder, NAudioRecorder>();
                services.AddTransient<IMicrophoneTester, MicrophoneTester>();
                services.AddHttpClient("Whisper.Download");
                services.AddSingleton<IWhisperTranscriber, WhisperTranscriber>();
                services.AddSingleton<PronunciationAssessor>();

                // ── view-models & windows (Agent B) ──
                // Content-pack import UI (§B1). The real IContentImportService is bound by
                // AddContentModule() above (Agent A, SP1); the launcher/VM/window only consume it.
                services.AddSingleton<ContentImportLauncher>();
                services.AddTransient<ContentImportViewModel>();
                services.AddTransient<ContentImportWindow>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<DictionaryViewModel>();
                services.AddSingleton<TrainerViewModel>();
                services.AddSingleton<StatsViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<Views.Settings.SettingsWindow>();

                // Reading module view models (transient — fresh state per navigation).
                services.AddSingleton<ReadingHubViewModel>();
                services.AddTransient<ReadingTestViewModel>();
                services.AddTransient<ReadingResultViewModel>();

                // Listening module view models — M8.
                services.AddSingleton<ListeningHubViewModel>();
                services.AddTransient<ListeningSessionViewModel>();
                services.AddTransient<ListeningResultViewModel>();

                // Writing module view models.
                services.AddSingleton<WritingHubViewModel>();
                services.AddTransient<WritingSessionViewModel>();
                services.AddTransient<WritingTaskViewModel>();
                services.AddTransient<WritingResultViewModel>();

                // Mock module view models — M11.
                services.AddSingleton<MockHubViewModel>();
                services.AddTransient<MockSessionViewModel>();
                services.AddTransient<MockResultViewModel>();
                services.AddTransient<WritingHistoryViewModel>();

                // Reading (study area) module view models.
                services.AddSingleton<ReadingLibraryViewModel>();
                services.AddSingleton<ReadingAppearanceViewModel>();
                services.AddTransient<ReaderViewModel>();
                services.AddTransient<ReadingSummaryViewModel>();
                services.AddTransient<PreTeachViewModel>();
                services.AddTransient<ComprehensionViewModel>();
                services.AddTransient<ShadowingViewModel>();
                services.AddTransient<NotesPanelViewModel>();
                services.AddTransient<ReadingProgressViewModel>();

                // All reading-study services are registered by the module chain above:
                //   .AddReadingStudyModule() — TextLibrary/TextLookup/Enrichment/Session, F1 PreTeach,
                //     F2 Comprehension, F3 Phoneme, F5 Notes, F6 ReadingStats, F8 TextPaginator.
                //   .AddReadingEngine()       — live IReadAlongController (Vosk), IReadingAnalysisService
                //     (Whisper), F4 TTS + Shadowing.
                // The UI-only Fake doubles were removed once the real services landed (CP2).

                // Speaking module view models — M10.
                services.AddSingleton<SpeakingHubViewModel>();
                services.AddTransient<SpeakingSessionViewModel>();
                services.AddTransient<SpeakingPart1ViewModel>();
                services.AddTransient<SpeakingPart2ViewModel>();
                services.AddTransient<SpeakingPart3ViewModel>();
                services.AddTransient<SpeakingResultViewModel>();

                // Shell + module descriptors
                services.AddSingleton<ShellViewModel>();
                RegisterModuleDescriptors(services);
            })
            .Build();

        // Apply the saved interface language before any window is shown.
        var localizer = _host.Services.GetRequiredService<ILocalizer>();
        var appSettings = _host.Services.GetRequiredService<IAppSettings>();
        localizer.SetLanguage(appSettings.Language);
        // The title-bar RU/EN toggle persists its choice through this hook (startup apply above does not).
        LocalizationManager.Instance.Persist = lang => appSettings.Update(new SettingsUpdate { Language = lang });

        var themeManager = _host.Services.GetRequiredService<IThemeManager>();
        var savedTheme = Enum.TryParse<AppTheme>(appSettings.Theme, out var theme) ? theme : AppTheme.DarkBlue;
        themeManager.Apply(savedTheme);

        try
        {
            await _host.StartAsync();

            using (var scope = _host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
                var ieltsFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IeltsDbContext>>();
                var readingFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ReadingDbContext>>();

                // All three contexts live in one SQLite file: one consistent copy before any of
                // them migrates protects months of user progress from a bad migration.
                await using (var ieltsDb = await ieltsFactory.CreateDbContextAsync())
                await using (var readingDb = await readingFactory.CreateDbContextAsync())
                {
                    var hasPending =
                        (await db.Database.GetPendingMigrationsAsync()).Any()
                        || (await ieltsDb.Database.GetPendingMigrationsAsync()).Any()
                        || (await readingDb.Database.GetPendingMigrationsAsync()).Any();
                    if (hasPending)
                    {
                        try
                        {
                            var backup = DatabaseBackup.Create("migration");
                            if (backup is not null)
                                _host.Services.GetRequiredService<ILogger<App>>()
                                    .LogInformation("Pre-migration DB backup: {Path}", backup);
                        }
                        catch (Exception ex)
                        {
                            _host.Services.GetRequiredService<ILogger<App>>()
                                .LogWarning(ex, "Pre-migration DB backup failed; continuing with migration.");
                        }
                    }

                    await db.Database.MigrateAsync();
                    await ieltsDb.Database.MigrateAsync();
                    await readingDb.Database.MigrateAsync();

                    // WAL is persisted in the DB file; readers no longer block the autosave writer
                    // ("database is locked" under concurrent debounced saves + background seeding).
                    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                }

                // ── content seeding (Agent A) ──
                // DB migrations above run unconditionally; content seeding is best-effort. On a fresh
                // install without an imported content-pack each IELTS/Oxford seeder soft-skips, and any
                // unexpected failure in one section must not block app startup — so each call is isolated.
                // AWL/AVL/categories are CC0 + embedded and always seed.
                // The whole pass is skipped when the SeedStamp token (app version + content manifest +
                // DB identity) matches the last fully-successful run — seeding only costs time after an
                // install, update, content import or DB reset.
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<App>>();

                if (SeedStamp.IsCurrent())
                {
                    logger.LogInformation("Seed stamp is current — skipping startup content seeding.");
                }
                else
                {
                    var allSeedsOk = true;

                    async Task SeedSafely(string name, Func<Task> run)
                    {
                        try { await run(); }
                        catch (Exception ex)
                        {
                            allSeedsOk = false;
                            logger.LogError(ex, "{Seed} failed during startup; continuing.", name);
                        }
                    }

                    var readingSeed = scope.ServiceProvider.GetRequiredService<ReadingSeedService>();
                    await SeedSafely("Reading seed", () => readingSeed.SeedIfMissingAsync());

                    var listeningSeed = scope.ServiceProvider.GetRequiredService<ListeningSeedService>();
                    await SeedSafely("Listening seed", () => listeningSeed.SeedIfMissingAsync());

                    var writingSeed = scope.ServiceProvider.GetRequiredService<WritingSeedService>();
                    await SeedSafely("Writing seed", () => writingSeed.SeedIfMissingAsync());

                    var speakingImport = scope.ServiceProvider.GetRequiredService<CambridgeSpeakingImportService>();
                    await SeedSafely("Speaking import", () => speakingImport.ImportIfPossibleAsync());

                    var seed = scope.ServiceProvider.GetRequiredService<SeedService>();
                    await SeedSafely("Dictionary Oxford seed", () => seed.SeedIfEmptyAsync());
                    await SeedSafely("Dictionary audio backfill", () => seed.BackfillAudioPathsAsync());
                    await SeedSafely("AWL seed", () => seed.SeedAwlIfEmptyAsync());
                    await SeedSafely("AVL seed", () => seed.SeedAvlIfEmptyAsync());
                    await SeedSafely("PHaVE seed", () => seed.SeedPhaveIfEmptyAsync());
                    await SeedSafely("IELTS categories seed", () => seed.SeedIeltsCategoriesIfEmptyAsync());

                    // Only a fully clean pass earns the stamp: a failed seeder retries next start.
                    if (allSeedsOk) SeedStamp.Save();
                }
                // ── content seeding END ──
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start the application:\n\n{ex.Message}",
                "English Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _host.Services.GetRequiredService<MainWindowViewModel>();
        mainWindow.Show();

        OfferCrashReportsIfAny();
    }

    /// <summary>
    /// If the previous run died with an unhandled fatal exception, offer to open the crash-report
    /// folder once; reports are then marked as seen so the prompt doesn't repeat.
    /// </summary>
    private static void OfferCrashReportsIfAny()
    {
        try
        {
            if (!CrashReporter.HasUnacknowledgedReports()) return;

            var open = MessageBox.Show(
                Loc.Tr("App_CrashPromptBody"),
                Loc.Tr("App_CrashPromptTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) == MessageBoxResult.Yes;

            CrashReporter.AcknowledgeAll();

            if (open)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = CrashReporter.CrashDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Best effort — never block startup over crash-report housekeeping.
        }
    }

    private static void RegisterModuleDescriptors(IServiceCollection services)
    {
        // Existing modules (M0–M6)
        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "dictionary",
            nameRu: "Словарь",
            nameEn: "Dictionary",
            iconGlyph: "📖",
            order: 10,
            viewFactory: sp => new DictionaryView { DataContext = sp.GetRequiredService<DictionaryViewModel>() },
            area: AppArea.Study));

        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "trainer",
            nameRu: "Тренажёр",
            nameEn: "Trainer",
            iconGlyph: "🎯",
            order: 20,
            viewFactory: sp => new TrainerView { DataContext = sp.GetRequiredService<TrainerViewModel>() },
            area: AppArea.Study));

        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "reading-study",
            nameRu: "Чтение",
            nameEn: "Reading",
            iconGlyph: "📕",
            order: 40,
            viewFactory: sp => new ReadingStudyModuleView
            {
                DataContext = sp.GetRequiredService<ReadingLibraryViewModel>()
            },
            area: AppArea.Study));

        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "stats",
            nameRu: "Статистика",
            nameEn: "Statistics",
            iconGlyph: "📊",
            order: 30,
            viewFactory: sp => new StatsView { DataContext = sp.GetRequiredService<StatsViewModel>() },
            area: AppArea.Global));

        // Reading module — M7
        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "reading",
            nameRu: "Reading",
            nameEn: "Reading",
            iconGlyph: "📚",
            order: 100,
            viewFactory: sp => new ReadingModuleView
            {
                DataContext = sp.GetRequiredService<ReadingHubViewModel>()
            },
            area: AppArea.Ielts));

        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "listening",
            nameRu: "Listening",
            nameEn: "Listening",
            iconGlyph: "🎧",
            order: 110,
            viewFactory: sp => new ListeningModuleView
            {
                DataContext = sp.GetRequiredService<ListeningHubViewModel>()
            },
            area: AppArea.Ielts));

        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "writing",
            nameRu: "Writing",
            nameEn: "Writing",
            iconGlyph: "✍",
            order: 120,
            viewFactory: sp => new WritingModuleView
            {
                DataContext = sp.GetRequiredService<WritingHubViewModel>()
            },
            area: AppArea.Ielts));

        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "speaking",
            nameRu: "Speaking",
            nameEn: "Speaking",
            iconGlyph: "🎤",
            order: 130,
            viewFactory: sp => new SpeakingModuleView
            {
                DataContext = sp.GetRequiredService<SpeakingHubViewModel>()
            },
            area: AppArea.Ielts));

        services.AddSingleton<IModuleDescriptor>(_ => new ModuleDescriptor(
            code: "mock-test",
            nameRu: "Полный экзамен",
            nameEn: "Full Exam",
            iconGlyph: "🏁",
            order: 200,
            viewFactory: sp => new MockModuleView
            {
                DataContext = sp.GetRequiredService<MockHubViewModel>()
            },
            area: AppArea.Ielts));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
