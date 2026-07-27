using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.GeneralSettings;

internal static class GeneralSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddGeneralSettingsFeature(this IServiceCollection services)
    {
        services.TryAddTransient<GeneralSettingsViewModel>();
        services.TryAddTransient<GeneralSettingsPage>();
        return services;
    }
}
