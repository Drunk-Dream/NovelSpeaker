using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.ChapterRules;

public static class ChapterRulesServiceCollectionExtensions
{
    public static IServiceCollection AddChapterRulesFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<ChapterRulesViewModel>();
        services.TryAddTransient<ChapterRulesPage>();
        return services;
    }
}
