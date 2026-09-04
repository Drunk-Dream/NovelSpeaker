using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Settings;

public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsFeature(this IServiceCollection services)
    {
        services.TryAddTransient<SettingsViewModel>();
        services.TryAddTransient<SettingsPage>();
        return services;
    }
}
