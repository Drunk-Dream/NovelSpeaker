using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.Books.Text;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class FileStorageRegistration
{
    public static IServiceCollection AddNovelSpeakerFileStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAppDataDirectoryProvider, LocalAppDataDirectoryProvider>();
        services.TryAddSingleton<IAppStoragePathResolver, AppStoragePathResolver>();
        services.TryAddSingleton<ITextFileAnalyzer, TextFileAnalyzer>();
        services.TryAddSingleton<IContentHasher, Sha256ContentHasher>();
        services.TryAddSingleton<IBookContentReader, BookContentReader>();
        services.TryAddSingleton<IBookFileStore, BookFileStore>();
        return services;
    }
}
