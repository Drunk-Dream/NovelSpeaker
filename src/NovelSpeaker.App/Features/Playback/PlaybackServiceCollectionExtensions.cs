using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Features.Playback.Scrolling;

namespace NovelSpeaker.App.Features.Playback;

public static class PlaybackServiceCollectionExtensions
{
    public static IServiceCollection AddPlaybackFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<IPlayerAutoScrollCoordinator, PlayerAutoScrollCoordinator>();
        services.TryAddSingleton<PlayerViewModel>();
        services.TryAddTransient<PlayerPage>();
        return services;
    }
}
