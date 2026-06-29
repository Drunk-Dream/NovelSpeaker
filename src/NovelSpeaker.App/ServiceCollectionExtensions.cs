using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App;

/// <summary>
/// Registers desktop-specific views and view models.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerDesktop(this IServiceCollection services)
    {
        services.AddSingleton<IAppNavigationService, AppNavigationService>();
        services.AddSingleton<IAppPageResolver, AppPageResolver>();
        services.AddSingleton<IThemeRuntime, WpfUiThemeRuntime>();
        services.AddSingleton<AppThemeStartupCoordinator>();
        services.AddSingleton<IFluentWindowAppearanceAdapter, FluentWindowAppearanceAdapter>();
        services.AddSingleton<IMainWindowAppearanceConfigurator, MainWindowAppearanceConfigurator>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ChapterRulesViewModel>();
        services.AddSingleton<TtsRulesViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<LibraryPage>();
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<PlayerPage>();
        services.AddSingleton<TtsRulesPage>();
        services.AddSingleton<ChapterRulesPage>();
        services.AddSingleton<BookDetailsPage>();
        services.AddSingleton<CacheManagementPage>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
