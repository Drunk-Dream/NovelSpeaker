using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.BookDetails;

public static class BookDetailsServiceCollectionExtensions
{
    public static IServiceCollection AddBookDetailsFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<IBookDeleteDialogService, BookDeleteDialogService>();
        services.TryAddTransient<BookDetailsViewModel>();
        services.TryAddTransient<BookDetailsPage>();
        return services;
    }
}
