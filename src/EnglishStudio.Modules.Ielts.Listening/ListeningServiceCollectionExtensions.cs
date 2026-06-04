using EnglishStudio.Modules.Ielts.Listening.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Modules.Ielts.Listening;

public static class ListeningServiceCollectionExtensions
{
    public static IServiceCollection AddIeltsListeningModule(this IServiceCollection services)
    {
        services.AddSingleton<ListeningSeedService>();
        services.AddSingleton<IListeningTestService, ListeningTestService>();
        services.AddSingleton<ListeningFeedbackService>();
        return services;
    }
}
