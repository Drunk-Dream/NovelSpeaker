using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Infrastructure.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.Books.Parsing;
using NovelSpeaker.Infrastructure.Books.Text;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Settings;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Infrastructure.Speech.Rules;
using NovelSpeaker.Infrastructure.Speech.Scripting;
using NovelSpeaker.Application.Speech;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services required for application startup.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppDataDirectoryProvider, LocalAppDataDirectoryProvider>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<SqliteMigrationRunner>();
        services.AddSingleton<IAudioPlayer, NaudioAudioPlayer>();
        services.AddSingleton<IAudioPlayerFactory, NaudioAudioPlayerFactory>();
        services.AddSingleton<ILocalAudioPlaybackCoordinator, LocalAudioPlaybackCoordinator>();
        services.AddSingleton<IPlaybackCoordinator, PlaybackCoordinator>();
        services.AddSingleton<IBookPlaybackContentService, BookPlaybackContentService>();
        services.AddSingleton<ISelectedTtsRuleProvider, SelectedTtsRuleProvider>();
        services.AddSingleton<IPlaybackAudioProvider, PlaybackAudioProvider>();
        services.AddSingleton(AudioCacheOptions.Default);
        services.AddSingleton<IAudioCacheProtectionRegistry, AudioCacheProtectionRegistry>();
        services.AddSingleton<SqliteAudioCache>();
        services.AddSingleton<IAudioCache>(serviceProvider => serviceProvider.GetRequiredService<SqliteAudioCache>());
        services.AddSingleton<IAudioCacheManagementService>(serviceProvider => serviceProvider.GetRequiredService<SqliteAudioCache>());
        services.AddSingleton<IPrefetchScheduler, PrefetchScheduler>();
        services.AddSingleton<IReadingProgressStore, SqliteReadingProgressStore>();
        services.AddSingleton<ITextSegmenter, TextSegmenter>();
        services.AddSingleton<IChapterRuleRepository, ChapterRuleRepository>();
        services.AddSingleton<IChapterRuleManagementService, ChapterRuleManagementService>();
        services.AddSingleton<ITtsRuleRepository, TtsRuleRepository>();
        services.AddSingleton<ITtsRuleConverter, LegadoRuleConverter>();
        services.AddSingleton<ITemplateEvaluator, JintTemplateEvaluator>();
        services.AddSingleton<ITtsRequestCompiler, TtsRequestCompiler>();
        services.AddSingleton<ITtsRateLimiter, TtsRateLimiter>();
        services.AddSingleton<IHttpTtsClient, HttpTtsClient>();
        services.AddSingleton<ITtsRuleTestService, TtsRuleTestService>();
        services.AddSingleton<ITtsRuleLibraryService, TtsRuleLibraryService>();
        services.AddSingleton<ITextFileAnalyzer, TextFileAnalyzer>();
        services.AddSingleton<ITextNormalizer, TextNormalizer>();
        services.AddSingleton<IContentHasher, Sha256ContentHasher>();
        services.AddSingleton<IChapterSplitter, ChapterSplitter>();
        services.AddSingleton<IBookContentReader, BookContentReader>();
        services.AddSingleton<IBookDuplicateDetector, BookDuplicateDetector>();
        services.AddSingleton<IBookImportRepository, BookImportRepository>();
        services.AddSingleton<IBookCatalogService, BookCatalogService>();
        services.AddSingleton<IBookManagementService, BookManagementService>();
        services.AddSingleton<IBookFileStore, BookFileStore>();
        services.AddSingleton<IBookImportService, BookImportService>();
        services.AddSingleton<JsonAppSettingsStore>();
        services.AddSingleton<IAppSettingsStore>(serviceProvider => serviceProvider.GetRequiredService<JsonAppSettingsStore>());
        services.AddSingleton<ITextSegmentationOptionsProvider>(serviceProvider => serviceProvider.GetRequiredService<JsonAppSettingsStore>());
        services.AddSingleton<DefaultChapterRuleSeeder>();
        services.AddSingleton<IDatabaseInitializer, StartupDatabaseInitializer>();

        return services;
    }
}
