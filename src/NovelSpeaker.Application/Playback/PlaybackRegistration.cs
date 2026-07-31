using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
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
        services.TryAddSingleton<CacheWorkspaceService>();
        services.TryAddSingleton<ICacheWorkspaceService>(
            serviceProvider => serviceProvider.GetRequiredService<CacheWorkspaceService>());
        services.TryAddSingleton<ICacheWorkspaceBackgroundTaskOwner>(
            serviceProvider => serviceProvider.GetRequiredService<CacheWorkspaceService>());
        services.TryAddSingleton<ExportFileNameSanitizer>();
        services.TryAddSingleton<IExportChaptersService, ExportChaptersService>();
        services.TryAddSingleton<IPlaybackAudioProvider, PlaybackAudioProvider>();
        services.TryAddSingleton<IActiveCacheCoordinator, ActiveCacheCoordinator>();
        services.TryAddSingleton<ILocalAudioPlaybackCoordinator, LocalAudioPlaybackCoordinator>();
        services.TryAddSingleton<PlaybackSegmentRunner>();
        services.TryAddSingleton<PlaybackRecoveryPolicy>();
        services.TryAddSingleton<PlaybackProgressService>();
        services.TryAddSingleton<IPlaybackPrefetchController, PlaybackPrefetchController>();
        services.TryAddSingleton<PlaybackCoordinator>(serviceProvider =>
            new PlaybackCoordinator(
                serviceProvider.GetRequiredService<IBookPlaybackContentService>(),
                serviceProvider.GetRequiredService<ISelectedTtsRuleProvider>(),
                serviceProvider.GetRequiredService<PlaybackSegmentRunner>(),
                serviceProvider.GetRequiredService<PlaybackRecoveryPolicy>(),
                serviceProvider.GetRequiredService<IAudioCacheProtectionRegistry>(),
                serviceProvider.GetRequiredService<ILocalAudioPlaybackCoordinator>(),
                serviceProvider.GetRequiredService<PlaybackProgressService>(),
                serviceProvider.GetRequiredService<IPlaybackPrefetchController>(),
                serviceProvider.GetRequiredService<IAppSettingsService>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
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
        services.TryAddSingleton<ISelectedTtsRuleProvider, SelectedTtsRuleProvider>();
        return services;
    }
}
