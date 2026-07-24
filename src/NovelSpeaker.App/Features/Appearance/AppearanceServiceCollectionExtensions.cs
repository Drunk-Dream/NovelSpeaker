using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Appearance;

public static class AppearanceServiceCollectionExtensions
{
    public static IServiceCollection AddAppearanceFeature(this IServiceCollection services)
    {
        services.TryAddTransient<AppearanceSettingsViewModel>();
        services.TryAddTransient<AppearanceSettingsPage>();
        return services;
    }
}
