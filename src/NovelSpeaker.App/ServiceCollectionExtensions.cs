using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Player;
using NovelSpeaker.App.Shell;
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
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IAppDialogService, AppDialogService>();
        services.AddSingleton<IAppNotificationService, AppNotificationService>();
        services.AddSingleton<IExceptionProjector, ExceptionProjector>();
        services.AddSingleton<IAppFeedbackService, AppFeedbackService>();
        services.AddSingleton<IImportBookDialogService, ImportBookDialogService>();
        services.AddSingleton<IBookDeleteDialogService, BookDeleteDialogService>();
        services.AddSingleton<IShellLayoutController, ShellLayoutController>();
        services.AddSingleton<IPlayerLayoutController, PlayerLayoutController>();
        services.AddSingleton<IPlayerAutoScrollCoordinator, PlayerAutoScrollCoordinator>();
        services.AddSingleton<IBookCoverGenerator, BookCoverGenerator>();
        services.AddSingleton<LibraryScrollState>();
        services.AddSingleton<IBookCatalogInvalidationState, BookCatalogInvalidationState>();
        services.AddSingleton<IThemeRuntime, WpfUiThemeRuntime>();
        services.AddSingleton<AppThemeStartupCoordinator>();
        services.AddSingleton<IThemePreferenceService, ThemePreferenceService>();
        services.AddSingleton<IFluentWindowAppearanceAdapter, FluentWindowAppearanceAdapter>();
        services.AddSingleton<IMainWindowAppearanceConfigurator, MainWindowAppearanceConfigurator>();
        services.AddSingleton<LibraryViewModel>();
        services.AddTransient<BookDetailsViewModel>();
        services.AddTransient<ImportBookDialogViewModel>();
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
