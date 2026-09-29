using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.ExperimentalFeatures;

internal static class ExperimentalFeaturesServiceCollectionExtensions
{
    public static IServiceCollection AddExperimentalFeaturesFeature(this IServiceCollection services)
    {
        services.TryAddTransient<ExperimentalFeaturesViewModel>();
        services.TryAddTransient<ExperimentalFeaturesPage>();
        return services;
    }
}
