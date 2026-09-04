using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Books.Shared;

public static class BooksSharedServiceCollectionExtensions
{
    public static IServiceCollection AddBooksSharedFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<IBookCoverGenerator, BookCoverGenerator>();
        services.TryAddSingleton<IBookCatalogInvalidationState, BookCatalogInvalidationState>();
        services.TryAddSingleton<IBookDeleteDialogService, BookDeleteDialogService>();
        return services;
    }
}
