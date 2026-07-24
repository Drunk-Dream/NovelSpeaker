using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Settings;

public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<SettingsViewModel>();
        services.TryAddTransient<SettingsPage>();
        return services;
    }
}
