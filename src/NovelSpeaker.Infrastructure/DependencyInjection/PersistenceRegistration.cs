using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Infrastructure.Persistence.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Infrastructure.Speech.Rules;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class PersistenceRegistration
{
    public static IServiceCollection AddNovelSpeakerPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.TryAddSingleton<IDatabaseSchemaVersionProvider, SqliteDatabaseSchemaVersionProvider>();
        services.TryAddSingleton<SqliteMigrationRunner>();
        services.TryAddSingleton<IChapterRuleRepository, ChapterRuleRepository>();
        services.TryAddSingleton<IRegexReplacementRuleRepository, RegexReplacementRuleRepository>();
        services.TryAddSingleton<ITtsRuleRepository, TtsRuleRepository>();
        services.TryAddSingleton<IBookImportRepository, BookImportRepository>();
        services.TryAddSingleton<IBookOperationJournal, SqliteBookOperationJournal>();
        services.TryAddSingleton<BookOperationRecoveryService>();
        services.TryAddSingleton<AppStoragePathMigrationService>();
        services.TryAddSingleton<AudioCacheFormatResetService>();
        services.TryAddSingleton<IBookLibraryQuery, BookLibraryQuery>();
        services.TryAddSingleton<IBookMetadataUpdateService, BookMetadataUpdateService>();
        services.TryAddSingleton<IBookDeletionOperationStore, BookDeletionOperationStore>();
        services.TryAddSingleton<IReadingProgressStore, SqliteReadingProgressStore>();
        services.TryAddSingleton<IChapterSpeechPlanStore, SqliteChapterSpeechPlanStore>();
        services.TryAddSingleton<DefaultChapterRuleSeeder>();
        services.TryAddSingleton<IDatabaseInitializer, StartupDatabaseInitializer>();

        return services;
    }
}
