using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books.ChapterRules;
using NovelSpeaker.Application.Books.Import;
using NovelSpeaker.Application.Books.Library;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Playback.Cache;

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
        services.TryAddSingleton<BookFileNameMetadataParser>();
        services.TryAddSingleton<ITextNormalizer, TextNormalizer>();
        services.TryAddSingleton<IChapterSplitter, ChapterSplitter>();
        services.TryAddSingleton<IBookImportIdGenerator, BookImportIdGenerator>();
        services.TryAddSingleton<IDirectBookImportService, DirectBookImportService>();
        services.TryAddSingleton<IBookDeletionService, BookDeletionService>();
        services.TryAddSingleton<IChapterRuleWorkspaceService, ChapterRuleWorkspaceService>();
        services.TryAddSingleton<IRegexReplacementRuleErrorStore, RegexReplacementRuleErrorStore>();
        services.TryAddSingleton<IRegexReplacementRuleWorkspaceService, RegexReplacementRuleWorkspaceService>();
        services.TryAddSingleton<IRegexReplacementPipeline, RegexReplacementPipeline>();
        services.TryAddSingleton<ITextSegmenter, TextSegmenter>();
        services.TryAddSingleton<IChapterSpeechPlanService, ChapterSpeechPlanService>();

        return services;
    }
}
