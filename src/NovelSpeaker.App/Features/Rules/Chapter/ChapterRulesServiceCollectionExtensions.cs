using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Rules.Chapter;

public static class ChapterRulesServiceCollectionExtensions
{
    public static IServiceCollection AddChapterRulesFeature(this IServiceCollection services)
    {
        services.TryAddTransient<ChapterRulesViewModel>();
        services.TryAddTransient<ChapterRulesPage>();
        return services;
    }
}
