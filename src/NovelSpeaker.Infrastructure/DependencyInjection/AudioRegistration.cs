using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Persistence.Playback;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Playback.Export;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class AudioRegistration
{
    public static IServiceCollection AddNovelSpeakerAudioAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAudioPlayer, NaudioAudioPlayer>();
        services.TryAddSingleton<IAudioPlayerFactory, NaudioAudioPlayerFactory>();
        services.TryAddSingleton<IPlaybackAudioFailureReporter, PlaybackAudioFailureReporter>();
        services.TryAddSingleton<IBookPlaybackContentFailureReporter, BookPlaybackContentFailureReporter>();
        services.TryAddSingleton<ICacheWorkspaceFailureReporter, CacheWorkspaceFailureReporter>();
        services.TryAddSingleton<IAudioCacheProtectionRegistry, AudioCacheProtectionRegistry>();
        services.TryAddSingleton<SqliteAudioCacheIndex>();
        services.TryAddSingleton<AudioCacheFileStore>();
        services.TryAddSingleton<AudioCacheMaintenance>();
        services.TryAddSingleton<AudioCacheFacade>();
        services.TryAddSingleton<IAudioCache>(provider => provider.GetRequiredService<AudioCacheFacade>());
        services.TryAddSingleton<IAudioCacheStore>(provider => provider.GetRequiredService<AudioCacheFacade>());
        services.TryAddSingleton<IChapterMp3Encoder, MediaFoundationChapterMp3Encoder>();
        services.TryAddSingleton<IChapterMp3ExportWriter, ChapterMp3ExportWriter>();
        return services;
    }
}
