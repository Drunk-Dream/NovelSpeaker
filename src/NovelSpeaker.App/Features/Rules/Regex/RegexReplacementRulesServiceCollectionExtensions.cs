using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Rules.Regex;

public static class RegexReplacementRulesServiceCollectionExtensions
{
    public static IServiceCollection AddRegexReplacementRulesFeature(this IServiceCollection services)
    {
        services.TryAddTransient<RegexReplacementRulesViewModel>();
        services.TryAddTransient<RegexReplacementRulesPage>();
        return services;
    }
}
