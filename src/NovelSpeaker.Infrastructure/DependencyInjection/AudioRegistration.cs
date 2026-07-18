using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Infrastructure.Playback;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class AudioRegistration
{
    public static IServiceCollection AddNovelSpeakerAudioAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAudioPlayer, NaudioAudioPlayer>();
        services.TryAddSingleton<IAudioPlayerFactory, NaudioAudioPlayerFactory>();
        services.TryAddSingleton<ILocalAudioPlaybackCoordinator, LocalAudioPlaybackCoordinator>();
        services.TryAddSingleton<IPlaybackCoordinator, PlaybackCoordinator>();
        services.TryAddSingleton<ISelectedTtsRuleProvider, SelectedTtsRuleProvider>();
        services.TryAddSingleton<IPlaybackAudioFailureReporter, PlaybackAudioFailureReporter>();
        services.TryAddSingleton<IAudioCacheProtectionRegistry, AudioCacheProtectionRegistry>();
        services.TryAddSingleton<SqliteAudioCache>();
        services.TryAddSingleton<IAudioCache>(provider => provider.GetRequiredService<SqliteAudioCache>());
        services.TryAddSingleton<IAudioCacheStore>(provider => provider.GetRequiredService<SqliteAudioCache>());
        services.TryAddSingleton<IPrefetchScheduler, PrefetchScheduler>();

        return services;
    }
}
