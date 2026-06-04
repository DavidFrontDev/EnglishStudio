using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Modules.Ielts.Mock;

public static class MockServiceCollectionExtensions
{
    public static IServiceCollection AddIeltsMockModule(this IServiceCollection services)
    {
        services.AddScoped<IMockSessionService, MockSessionService>();
        services.AddScoped<MockBundlePicker>();
        return services;
    }
}
