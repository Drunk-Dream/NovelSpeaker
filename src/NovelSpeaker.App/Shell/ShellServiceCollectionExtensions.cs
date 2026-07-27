using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace NovelSpeaker.App.Shell;

public static class ShellServiceCollectionExtensions
{
    public static IServiceCollection AddShellServices(this IServiceCollection services)
    {
        services.TryAddSingleton<INavigationViewPageProvider, AppNavigationPageProvider>();
        services.TryAddSingleton<INavigationService, NavigationService>();
        services.TryAddSingleton<INavigationGuardService, NavigationGuardService>();
        services.TryAddSingleton<ShellNavigationAdapter>();
        services.TryAddSingleton<IShellNavigationAdapter>(provider => provider.GetRequiredService<ShellNavigationAdapter>());
        services.TryAddSingleton<IAppNavigator>(provider => provider.GetRequiredService<ShellNavigationAdapter>());
        services.TryAddSingleton<IKeyboardShortcutCoordinator, KeyboardShortcutCoordinator>();
        services.TryAddSingleton<IShortcutContextResolver, WpfShortcutContextResolver>();
        services.TryAddSingleton<IShellPlatformAdapter, WpfShellPlatformAdapter>();
        services.TryAddSingleton<IShellActivationCoordinator, ShellActivationCoordinator>();
        services.TryAddSingleton<IShellLayoutController, ShellLayoutController>();
        services.TryAddSingleton<ShellActiveCacheController>();
        services.TryAddSingleton<MainWindowViewModel>();
        services.TryAddSingleton<MainWindow>();
        return services;
    }
}
