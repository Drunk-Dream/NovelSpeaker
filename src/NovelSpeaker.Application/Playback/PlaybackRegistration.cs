using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Application.Speech.Testing;
using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Defines the composition boundary for playback application use cases.
/// </summary>
public static class PlaybackRegistration
{
    public static IServiceCollection AddNovelSpeakerPlaybackApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IBookPlaybackContentService, PlaybackContentResolver>();
        services.TryAddSingleton<ITtsRulePreviewAudioPlayer, TtsRulePreviewAudioPlayer>();
        services.TryAddSingleton<ILocalAudioPlaybackCoordinator, LocalAudioPlaybackCoordinator>();
        services.TryAddSingleton<PlaybackAudioController>(serviceProvider =>
            new PlaybackAudioController(serviceProvider.GetRequiredService<ILocalAudioPlaybackCoordinator>()));
        services.TryAddSingleton<PlaybackSegmentRunner>();
        services.TryAddSingleton<PlaybackRecoveryPolicy>();
        services.TryAddSingleton<PlaybackProgressController>();
        services.TryAddSingleton<IPlaybackPrefetchController, PlaybackPrefetchCoordinator>();
        services.TryAddSingleton<PlaybackCoordinator>(serviceProvider =>
            new PlaybackCoordinator(
                serviceProvider.GetRequiredService<IBookPlaybackContentService>(),
                serviceProvider.GetRequiredService<ISelectedTtsRuleProvider>(),
                serviceProvider.GetRequiredService<PlaybackSegmentRunner>(),
                serviceProvider.GetRequiredService<PlaybackRecoveryPolicy>(),
                serviceProvider.GetRequiredService<IAudioCacheProtectionRegistry>(),
                serviceProvider.GetRequiredService<PlaybackAudioController>(),
                serviceProvider.GetRequiredService<PlaybackProgressController>(),
                serviceProvider.GetRequiredService<IPlaybackPrefetchController>(),
                serviceProvider.GetRequiredService<IAppSettingsService>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<IObservability>()));
        services.TryAddSingleton<IPlaybackSnapshotSource>(serviceProvider =>
            serviceProvider.GetRequiredService<PlaybackCoordinator>());
        services.TryAddSingleton<IPlaybackSession>(serviceProvider =>
            serviceProvider.GetRequiredService<PlaybackCoordinator>());
        services.TryAddSingleton<IPlaybackStopTimer>(serviceProvider =>
            serviceProvider.GetRequiredService<PlaybackCoordinator>());
        services.TryAddSingleton<IPlaybackBookCommands>(serviceProvider =>
            serviceProvider.GetRequiredService<PlaybackCoordinator>());
        services.TryAddSingleton<IPlaybackRegexReplacementRefresher>(serviceProvider =>
            serviceProvider.GetRequiredService<PlaybackCoordinator>());
        return services;
    }
}
