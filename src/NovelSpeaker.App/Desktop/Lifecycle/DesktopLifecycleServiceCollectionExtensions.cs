using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Desktop.Lifecycle;

internal static class DesktopLifecycleServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopLifecycle(this IServiceCollection services)
    {
        services.TryAddSingleton<IDesktopExitGuard, NavigationDesktopExitGuard>();
        services.TryAddSingleton<IProcessShutdownRequest, ProcessShutdownRequest>();
        services.TryAddSingleton<IDesktopLifecyclePlatform, WindowsTrayLifecycleAdapter>();
        services.TryAddSingleton<IDesktopLifecycleCoordinator, DesktopLifecycleCoordinator>();
        return services;
    }
}
