using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Books;
using NovelSpeaker.Infrastructure.Books.Parsing;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class BooksRegistration
{
    public static IServiceCollection AddNovelSpeakerBooksAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ITextSegmenter, TextSegmenter>();
        services.TryAddSingleton<IChapterRuleManagementService, ChapterRuleManagementService>();
        services.TryAddSingleton<IChapterRuleWorkspaceService, ChapterRuleWorkspaceService>();
        services.TryAddSingleton<IRegexReplacementRuleWorkspaceService, RegexReplacementRuleWorkspaceService>();
        services.TryAddSingleton<IRegexReplacementPipeline, RegexReplacementPipeline>();
        services.TryAddSingleton<IBookDuplicateDetector, BookDuplicateDetector>();
        services.TryAddSingleton<IDirectBookImportService, DirectBookImportService>();

        return services;
    }
}
