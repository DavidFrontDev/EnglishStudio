using System.Net.Http.Headers;
using EnglishStudio.Modules.Dictionary.Audio;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Images;
using EnglishStudio.Modules.Dictionary.Localization;
using EnglishStudio.Modules.Dictionary.Seed;
using EnglishStudio.Modules.Dictionary.Srs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.Extensions.Http;

namespace EnglishStudio.Modules.Dictionary;

public static class DictionaryServiceCollectionExtensions
{
    public static IServiceCollection AddDictionaryModule(this IServiceCollection services)
    {
        DictionaryPaths.EnsureDirectoriesExist();

        // Единый источник правды о наличии импортированного контента (см. план §2).
        services.AddSingleton<IContentStore, FileSystemContentStore>();

        // Хост (App) перекрывает это реальным локализатором; в тестах/тулинге работает key-echo.
        services.TryAddSingleton<IMessageLocalizer, KeyEchoMessageLocalizer>();

        services.AddDbContext<DictionaryDbContext>(opt =>
            opt.UseSqlite(DictionaryPaths.SqliteConnectionString));

        services.AddScoped<SeedService>();

        services.AddHttpClient(AudioCacheService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("EnglishStudio", "1.0"));
            })
            .AddPolicyHandler(GetAudioRetryPolicy());

        services.AddSingleton<IAudioCacheService, AudioCacheService>();

        services.AddSingleton<IAppSettings, AppSettings>();

        services.AddHttpClient(WikimediaImageProvider.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EnglishStudio", "1.0"));
        });
        services.AddHttpClient(PexelsImageProvider.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EnglishStudio", "1.0"));
        });
        services.AddHttpClient(ImageCacheService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromMinutes(2);
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EnglishStudio", "1.0"));
        });

        services.AddSingleton<IImageProvider, WikimediaImageProvider>();
        services.AddSingleton<IImageProvider, PexelsImageProvider>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();

        services.AddSingleton(sp => new FsrsParameters
        {
            TargetRetention = Math.Clamp(
                sp.GetRequiredService<IAppSettings>().TargetRetention, 0.7, 0.99),
        });
        services.AddSingleton<IFsrsScheduler, FsrsScheduler>();
        services.AddSingleton<ISrsService, SrsService>();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetAudioRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt - 1)));
    }
}
