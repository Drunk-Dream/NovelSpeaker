using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books.ChapterRules;
using NovelSpeaker.Application.Books.Import;
using NovelSpeaker.Application.Books.Library;
using NovelSpeaker.Application.Books.TextProcessing;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Defines the composition boundary for book application use cases.
/// </summary>
public static class BooksRegistration
{
    public static IServiceCollection AddNovelSpeakerBooksApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IChapterRuleManagementService, ChapterRuleManagementService>();
        services.TryAddSingleton<ImportMetadataExtractor>();
        services.TryAddSingleton<ITextNormalizer, TextNormalizer>();
        services.TryAddSingleton<IChapterSplitter, ChapterSplitter>();
        services.TryAddSingleton<IBookImportIdGenerator, BookImportIdGenerator>();
        services.TryAddSingleton<BookMutationGate>();
        services.TryAddSingleton<BookSourceChanges>();
        services.TryAddSingleton<DirectBookImportService>();
        services.TryAddSingleton<IDirectBookImportService>(provider => provider.GetRequiredService<DirectBookImportService>());
        services.TryAddSingleton<IBookSourceChangeSource>(provider => provider.GetRequiredService<BookSourceChanges>());
        services.TryAddSingleton<BookDeletionService>();
        services.TryAddSingleton<IBookDeletionService>(provider => provider.GetRequiredService<BookDeletionService>());
        services.TryAddSingleton<IBookSourceRemovalService>(provider => provider.GetRequiredService<BookDeletionService>());
        services.TryAddSingleton<IChapterRuleWorkspaceService, ChapterRuleWorkspaceService>();
        services.TryAddSingleton<IRegexReplacementRuleErrorStore, RegexReplacementRuleErrorStore>();
        services.TryAddSingleton<RegexReplacementRuleWorkspaceService>();
        services.TryAddSingleton<IRegexReplacementRuleWorkspaceService>(provider => provider.GetRequiredService<RegexReplacementRuleWorkspaceService>());
        services.TryAddSingleton<IRegexReplacementPipeline, RegexReplacementPipeline>();
        services.TryAddSingleton<ITextSegmenter, TextSegmenter>();
        return services;
    }
}
