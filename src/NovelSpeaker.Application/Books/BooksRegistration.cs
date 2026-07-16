using Microsoft.Extensions.DependencyInjection;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Defines the composition boundary for book application use cases.
/// </summary>
public static class BooksRegistration
{
    public static IServiceCollection AddNovelSpeakerBooksApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
