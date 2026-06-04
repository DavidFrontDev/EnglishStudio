using EnglishStudio.App.Reading.Audio;
using EnglishStudio.App.Reading.Tts;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.App.Reading;

public static class ReadingEngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reading-module live engine (Vosk read-along + Whisper post-analysis).
    /// Depends on services registered elsewhere: <c>IWhisperTranscriber</c>,
    /// <c>PronunciationAssessor</c>, <c>IAppSettings</c> (dictionary module).
    /// </summary>
    public static IServiceCollection AddReadingEngine(this IServiceCollection services)
    {
        services.AddHttpClient("Reading.VoskDownload");

        // Model is loaded once and shared; each session gets its own capture + recognizer.
        services.AddSingleton<VoskSpeechRecognizer>();
        services.AddTransient<IReadingAudioCapture, NAudioReadingCapture>();
        services.AddTransient<IReadAlongController, VoskReadAlongController>();
        services.AddSingleton<IReadingAnalysisService, WhisperReadingAnalysisService>();

        // F4 shadowing: TTS playback + repeat scoring (reuses capture + Whisper analysis).
        services.AddSingleton<ITtsService, SystemSpeechTtsService>();
        services.AddSingleton<IShadowingService, ShadowingService>();

        return services;
    }
}
