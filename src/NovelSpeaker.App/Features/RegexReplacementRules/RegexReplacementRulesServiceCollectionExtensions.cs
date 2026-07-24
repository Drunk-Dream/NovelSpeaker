using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.RegexReplacementRules;

public static class RegexReplacementRulesServiceCollectionExtensions
{
    public static IServiceCollection AddRegexReplacementRulesFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<RegexReplacementRulesViewModel>();
        services.TryAddTransient<RegexReplacementRulesPage>();
        return services;
    }
}
