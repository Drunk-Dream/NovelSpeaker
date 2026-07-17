using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class BooksRegistration
{
    public static IServiceCollection AddNovelSpeakerBooksAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IBookDuplicateDetector, BookDuplicateDetector>();
        services.TryAddSingleton<IBookPlaybackMetadataQuery, SqliteBookPlaybackMetadataQuery>();
        return services;
    }
}
