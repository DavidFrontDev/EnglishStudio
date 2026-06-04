using EnglishStudio.Modules.Ielts.Reading.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Modules.Ielts.Reading;

public static class ReadingServiceCollectionExtensions
{
    public static IServiceCollection AddIeltsReadingModule(this IServiceCollection services)
    {
        services.AddSingleton<ReadingSeedService>();
        services.AddSingleton<IReadingTestService, ReadingTestService>();
        return services;
    }
}
