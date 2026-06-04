using EnglishStudio.Modules.Ai.Evaluators;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishStudio.Modules.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiModule(this IServiceCollection services)
    {
        services.AddOptions<ClaudeCliOptions>();
        services.AddSingleton<IClaudeCliClient, ClaudeCliClient>();
        services.AddSingleton<IIeltsEssayEvaluator, ClaudeIeltsEssayEvaluator>();
        services.AddSingleton<IIeltsSpeakingEvaluator, ClaudeIeltsSpeakingEvaluator>();
        services.AddSingleton<IIeltsListeningEvaluator, ClaudeIeltsListeningEvaluator>();
        return services;
    }
}
