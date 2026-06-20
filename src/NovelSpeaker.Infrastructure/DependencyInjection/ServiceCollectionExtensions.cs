using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Infrastructure.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.Books.Parsing;
using NovelSpeaker.Infrastructure.Books.Text;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Settings;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services required for application startup.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppDataDirectoryProvider, LocalAppDataDirectoryProvider>();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<SqliteMigrationRunner>();
        services.AddSingleton<ITextSegmenter, TextSegmenter>();
        services.AddSingleton<IChapterRuleRepository, ChapterRuleRepository>();
        services.AddSingleton<ITextFileAnalyzer, TextFileAnalyzer>();
        services.AddSingleton<ITextNormalizer, TextNormalizer>();
        services.AddSingleton<IContentHasher, Sha256ContentHasher>();
        services.AddSingleton<IChapterSplitter, ChapterSplitter>();
        services.AddSingleton<IBookDuplicateDetector, BookDuplicateDetector>();
        services.AddSingleton<IBookImportRepository, BookImportRepository>();
        services.AddSingleton<IBookCatalogService, BookCatalogService>();
        services.AddSingleton<IBookFileStore, BookFileStore>();
        services.AddSingleton<IBookImportService, BookImportService>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<ITextSegmentationOptionsProvider>(serviceProvider =>
            (JsonAppSettingsStore)serviceProvider.GetRequiredService<IAppSettingsStore>());
        services.AddSingleton<DefaultChapterRuleSeeder>();
        services.AddSingleton<IDatabaseInitializer, StartupDatabaseInitializer>();

        return services;
    }
}
