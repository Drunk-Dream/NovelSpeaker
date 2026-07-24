using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.TtsRules;

public static class TtsRulesServiceCollectionExtensions
{
    public static IServiceCollection AddTtsRulesFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<TtsRulesViewModel>();
        services.TryAddTransient<TtsRulesPage>();
        return services;
    }
}
