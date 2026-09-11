using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.Infrastructure.Cache;
using NovelSpeaker.Infrastructure.Cache.Export;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Persistence.Cache;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure adapters owned by the Cache application module.
/// </summary>
public static class CacheRegistration
{
    public static IServiceCollection AddNovelSpeakerCacheAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAudioGenerationFailureReporter, AudioGenerationFailureReporter>();
        services.TryAddSingleton<ICacheCompletenessFailureReporter, CacheCompletenessFailureReporter>();
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
