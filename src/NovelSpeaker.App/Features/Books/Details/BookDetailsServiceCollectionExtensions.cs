using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Books.Details;

public static class BookDetailsServiceCollectionExtensions
{
    public static IServiceCollection AddBookDetailsFeature(this IServiceCollection services)
    {
        services.TryAddTransient<BookDetailsViewModel>();
        services.TryAddTransient<BookDetailsPage>();
        return services;
    }
}
