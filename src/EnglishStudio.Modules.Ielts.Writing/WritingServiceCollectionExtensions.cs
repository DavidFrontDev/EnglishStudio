using EnglishStudio.Modules.Ielts.Writing.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Modules.Ielts.Writing;

public static class WritingServiceCollectionExtensions
{
    public static IServiceCollection AddIeltsWritingModule(this IServiceCollection services)
    {
        services.AddSingleton<WritingSeedService>();
        services.AddSingleton<IWritingTaskService, WritingTaskService>();
        services.AddSingleton<WritingFeedbackService>();
        return services;
    }
}
