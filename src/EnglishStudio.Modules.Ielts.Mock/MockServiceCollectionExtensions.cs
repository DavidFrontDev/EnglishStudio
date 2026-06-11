using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Modules.Ielts.Mock;

public static class MockServiceCollectionExtensions
{
    public static IServiceCollection AddIeltsMockModule(this IServiceCollection services)
    {
        services.AddSingleton<IMockSessionService, MockSessionService>();
        services.AddSingleton<MockBundlePicker>();
        return services;
    }
}
