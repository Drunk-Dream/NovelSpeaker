using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.App.Desktop.MiniPlayer;

namespace NovelSpeaker.App.Desktop.Lifecycle;

internal static class DesktopLifecycleServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopLifecycle(this IServiceCollection services)
    {
        services.TryAddSingleton<IDesktopExitGuard, NavigationDesktopExitGuard>();
        services.TryAddSingleton<IProcessShutdownRequest, ProcessShutdownRequest>();
        services.TryAddSingleton<IMiniPlayerScreenBoundsProvider, WindowsMiniPlayerScreenBoundsProvider>();
        services.TryAddSingleton<WindowsTrayLifecycleAdapter>();
        services.TryAddSingleton<IDesktopLifecyclePlatform>(serviceProvider =>
            serviceProvider.GetRequiredService<WindowsTrayLifecycleAdapter>());
        services.TryAddSingleton<DesktopLifecycleCoordinator>();
        services.TryAddSingleton<IDesktopLifecycleCoordinator>(serviceProvider =>
            serviceProvider.GetRequiredService<DesktopLifecycleCoordinator>());
        services.TryAddSingleton<IMiniPlayerLauncher>(serviceProvider =>
            serviceProvider.GetRequiredService<DesktopLifecycleCoordinator>());
        services.TryAddSingleton<MiniPlayerViewModel>();
        services.TryAddSingleton<IMiniPlayerPlacementPersistence>(serviceProvider =>
            serviceProvider.GetRequiredService<MiniPlayerViewModel>());
        services.TryAddSingleton<MiniPlayerWindow>();
        return services;
    }
}
