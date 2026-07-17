using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Speech.Rules;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class PersistenceRegistration
{
    public static IServiceCollection AddNovelSpeakerPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.TryAddSingleton<SqliteMigrationRunner>();
        services.TryAddSingleton<IChapterRuleRepository, ChapterRuleRepository>();
        services.TryAddSingleton<IRegexReplacementRuleRepository, RegexReplacementRuleRepository>();
        services.TryAddSingleton<ITtsRuleRepository, TtsRuleRepository>();
        services.TryAddSingleton<IBookImportRepository, BookImportRepository>();
        services.TryAddSingleton<IBookLibraryQuery, BookLibraryQuery>();
        services.TryAddSingleton<IBookMetadataUpdateService, BookMetadataUpdateService>();
        services.TryAddSingleton<IBookDeletionService, BookDeletionService>();
        services.TryAddSingleton<IReadingProgressStore, SqliteReadingProgressStore>();
        services.TryAddSingleton<DefaultChapterRuleSeeder>();
        services.TryAddSingleton<IDatabaseInitializer, StartupDatabaseInitializer>();

        return services;
    }
}
