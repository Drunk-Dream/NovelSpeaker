using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace NovelSpeaker.App;

/// <summary>
/// Registers desktop-specific views and view models.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerDesktop(this IServiceCollection services)
    {
        services.AddSingleton<INavigationViewPageProvider, AppNavigationPageProvider>();
        services.AddSingleton<INavigationService, NavigationService>();
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
        services.AddTransient<LibraryPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<PlayerPage>();
        services.AddTransient<TtsRulesPage>();
        services.AddTransient<ChapterRulesPage>();
        services.AddTransient<BookDetailsPage>();
        services.AddTransient<CacheManagementPage>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
