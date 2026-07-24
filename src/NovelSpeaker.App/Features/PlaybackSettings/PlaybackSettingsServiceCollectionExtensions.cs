using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.PlaybackSettings;

public static class PlaybackSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddPlaybackSettingsFeature(this IServiceCollection services)
    {
        services.TryAddTransient<PlaybackSettingsViewModel>();
        services.TryAddTransient<PlaybackSettingsPage>();
        return services;
    }
}
