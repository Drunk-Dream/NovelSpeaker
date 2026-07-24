using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Library;

public static class LibraryServiceCollectionExtensions
{
    public static IServiceCollection AddLibraryFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<IEncodingSelectionDialogService, EncodingSelectionDialogService>();
        services.TryAddSingleton<IImportProgressDialogService, ImportProgressDialogService>();
        services.TryAddSingleton<ILibraryImportCoordinator, LibraryImportCoordinator>();
        services.TryAddSingleton<LibraryScrollState>();
        services.TryAddSingleton<LibraryViewModel>();
        services.TryAddTransient<LibraryPage>();
        return services;
    }
}
