using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Defines the composition boundary for Cache-owned queries, stores and background jobs.
/// </summary>
public static class CacheRegistration
{
    public static IServiceCollection AddNovelSpeakerCacheApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICacheInvalidationCoordinator, CacheInvalidationCoordinator>();
        services.TryAddSingleton<ICacheCatalog, CacheCatalog>();
        services.TryAddSingleton<ICacheCoverageQuery, CacheCoverageQuery>();
        services.TryAddSingleton<IChapterSpeechPlanService, ChapterSpeechPlanService>();
        services.TryAddSingleton<ISpeechPlanRepairCoordinator, SpeechPlanRepairCoordinator>();
        services.TryAddSingleton<ICachePlanRepairRequestor, SpeechPlanRepairRequestor>();
        services.TryAddSingleton<IAudioCacheLimitProvider, SettingsCacheLimitProvider>();
        services.TryAddSingleton<IAudioGenerationProvider, CacheAudioGenerationProvider>();
        services.TryAddSingleton<ExportFileNameSanitizer>();
        services.TryAddSingleton<IExportChaptersService, ExportChaptersService>();
        services.TryAddSingleton<IChapterExportCoordinator, ChapterExportCoordinator>();
        services.TryAddSingleton<IActiveCacheCoordinator, ActiveCacheCoordinator>();

        return services;
    }

    private sealed class SettingsCacheLimitProvider(IAppSettingsService settingsService) : IAudioCacheLimitProvider
    {
        public long GetCurrentLimitBytes() => settingsService.Current.CacheLimitBytes;
    }
}
