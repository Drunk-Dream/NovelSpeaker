using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Diagnostics;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Input;
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
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<INavigationViewPageProvider, AppNavigationPageProvider>();
        services.TryAddSingleton<INavigationService, NavigationService>();
        services.TryAddSingleton<INavigationGuardService, NavigationGuardService>();
        services.TryAddSingleton<IGuardedNavigationService, GuardedNavigationService>();
        services.TryAddSingleton<IContentDialogService, ContentDialogService>();
        services.TryAddSingleton<ISnackbarService, SnackbarService>();
        services.TryAddSingleton<IAppDialogService, AppDialogService>();
        services.TryAddSingleton<IAppNotificationService, AppNotificationService>();
        services.TryAddSingleton<IExceptionProjector, ExceptionProjector>();
        services.TryAddSingleton<IAppFeedbackService, AppFeedbackService>();
        services.TryAddSingleton<ITextFilePicker, TextFilePicker>();
        services.TryAddSingleton<IKeyboardShortcutCoordinator, KeyboardShortcutCoordinator>();
        services.TryAddSingleton<IAppDiagnosticsService, AppDiagnosticsService>();
        services.TryAddSingleton<IClipboardService, WpfClipboardService>();
        services.TryAddSingleton<IEncodingSelectionDialogService, EncodingSelectionDialogService>();
        services.TryAddSingleton<IImportProgressDialogService, ImportProgressDialogService>();
        services.TryAddSingleton<IBookDeleteDialogService, BookDeleteDialogService>();
        services.TryAddSingleton<IShellLayoutController, ShellLayoutController>();
        services.TryAddSingleton<IPlayerAutoScrollCoordinator, PlayerAutoScrollCoordinator>();
        services.TryAddSingleton<IBookCoverGenerator, BookCoverGenerator>();
        services.TryAddSingleton<LibraryScrollState>();
        services.TryAddSingleton<ILibraryImportCoordinator, LibraryImportCoordinator>();
        services.TryAddSingleton<IBookCatalogInvalidationState, BookCatalogInvalidationState>();
        services.TryAddSingleton<IThemeRuntime, WpfUiThemeRuntime>();
        services.TryAddSingleton<AppThemeStartupCoordinator>();
        services.TryAddSingleton<IThemePreferenceService, ThemePreferenceService>();
        services.TryAddSingleton<IFluentWindowAppearanceAdapter, FluentWindowAppearanceAdapter>();
        services.TryAddSingleton<IMainWindowAppearanceConfigurator, MainWindowAppearanceConfigurator>();
        services.TryAddSingleton<LibraryViewModel>();
        services.TryAddTransient<BookDetailsViewModel>();
        services.TryAddSingleton<PlayerViewModel>();
        services.TryAddSingleton<SettingsViewModel>();
        services.TryAddTransient<CacheAndDataViewModel>();
        services.TryAddTransient<CacheManagementViewModel>();
        services.TryAddTransient<PlaybackSettingsViewModel>();
        services.TryAddTransient<ImportTextSettingsViewModel>();
        services.TryAddSingleton<RegexReplacementRulesViewModel>();
        services.TryAddTransient<AppearanceSettingsViewModel>();
        services.TryAddTransient<DiagnosticsAboutViewModel>();
        services.TryAddSingleton<ChapterRulesViewModel>();
        services.TryAddSingleton<TtsRulesViewModel>();
        services.TryAddSingleton<MainWindowViewModel>();
        services.TryAddTransient<LibraryPage>();
        services.TryAddTransient<SettingsPage>();
        services.TryAddTransient<CacheAndDataPage>();
        services.TryAddTransient<PlaybackSettingsPage>();
        services.TryAddTransient<ImportTextSettingsPage>();
        services.TryAddTransient<RegexReplacementRulesPage>();
        services.TryAddTransient<AppearanceSettingsPage>();
        services.TryAddTransient<DiagnosticsAboutPage>();
        services.TryAddTransient<PlayerPage>();
        services.TryAddTransient<TtsRulesPage>();
        services.TryAddTransient<ChapterRulesPage>();
        services.TryAddTransient<BookDetailsPage>();
        services.TryAddTransient<CacheManagementPage>();
        services.TryAddSingleton<MainWindow>();
        return services;
    }
}
