using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Books;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui;

namespace NovelSpeaker.App.Shared;

public static class SharedServiceCollectionExtensions
{
    public static IServiceCollection AddSharedServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IContentDialogService, ContentDialogService>();
        services.TryAddSingleton<ISnackbarService, SnackbarService>();
        services.TryAddSingleton<IAppDialogService, AppDialogService>();
        services.TryAddSingleton<IAppNotificationService, AppNotificationService>();
        services.TryAddSingleton<IExceptionProjector, ExceptionProjector>();
        services.TryAddSingleton<IAppFeedbackService, AppFeedbackService>();
        services.TryAddSingleton<PageEventOperationRunner>();
        services.TryAddSingleton<IUiScheduler, WpfUiScheduler>();
        services.TryAddSingleton<IPresentationFileDialogService, WpfPresentationFileDialogService>();
        services.TryAddSingleton<IPresentationClipboard, WpfPresentationClipboard>();
        services.TryAddSingleton<IRuleDocumentInteraction, RuleDocumentInteraction>();
        services.TryAddSingleton<IPresentationLauncher, ShellPresentationLauncher>();
        services.TryAddSingleton<IBookCoverGenerator, BookCoverGenerator>();
        services.TryAddSingleton<IBookCatalogInvalidationState, BookCatalogInvalidationState>();
        services.TryAddSingleton<IThemeRuntime, WpfUiThemeRuntime>();
        services.TryAddSingleton<AppThemeStartupCoordinator>();
        services.TryAddSingleton<ThemePreferenceService>();
        services.TryAddSingleton<IFluentWindowAppearanceAdapter, FluentWindowAppearanceAdapter>();
        services.TryAddSingleton<IMainWindowAppearanceConfigurator, MainWindowAppearanceConfigurator>();
        return services;
    }
}
