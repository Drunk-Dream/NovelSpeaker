using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Defines the composition boundary for playback application use cases.
/// </summary>
public static class PlaybackRegistration
{
    public static IServiceCollection AddNovelSpeakerPlaybackApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IBookPlaybackContentService, BookPlaybackContentService>();
        services.TryAddSingleton<ICacheWorkspaceService, CacheWorkspaceService>();
        services.TryAddSingleton<IPlaybackAudioProvider, PlaybackAudioProvider>();
        services.TryAddSingleton<ILocalAudioPlaybackCoordinator, LocalAudioPlaybackCoordinator>();
        services.TryAddSingleton<PlaybackSegmentRunner>();
        services.TryAddSingleton<PlaybackRecoveryPolicy>();
        services.TryAddSingleton<IPlaybackCoordinator>(serviceProvider =>
            new PlaybackCoordinator(
                serviceProvider.GetRequiredService<IBookPlaybackContentService>(),
                serviceProvider.GetRequiredService<ISelectedTtsRuleProvider>(),
                serviceProvider.GetRequiredService<PlaybackSegmentRunner>(),
                serviceProvider.GetRequiredService<PlaybackRecoveryPolicy>(),
                serviceProvider.GetRequiredService<IAudioCacheProtectionRegistry>(),
                serviceProvider.GetRequiredService<ILocalAudioPlaybackCoordinator>(),
                serviceProvider.GetRequiredService<IReadingProgressStore>(),
                serviceProvider.GetRequiredService<IPrefetchScheduler>(),
                serviceProvider.GetRequiredService<IAppSettingsService>()));
        services.TryAddSingleton<IPrefetchScheduler, PrefetchScheduler>();
        services.TryAddSingleton<ISelectedTtsRuleProvider, SelectedTtsRuleProvider>();
        return services;
    }
}
