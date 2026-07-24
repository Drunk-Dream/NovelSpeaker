using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Books;
using NovelSpeaker.App.Shared.Theming;
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
        services.TryAddSingleton<ITextFilePicker, TextFilePicker>();
        services.TryAddSingleton<IBookCoverGenerator, BookCoverGenerator>();
        services.TryAddSingleton<IBookCatalogInvalidationState, BookCatalogInvalidationState>();
        services.TryAddSingleton<IThemeRuntime, WpfUiThemeRuntime>();
        services.TryAddSingleton<AppThemeStartupCoordinator>();
        services.TryAddSingleton<IThemePreferenceService, ThemePreferenceService>();
        services.TryAddSingleton<IFluentWindowAppearanceAdapter, FluentWindowAppearanceAdapter>();
        services.TryAddSingleton<IMainWindowAppearanceConfigurator, MainWindowAppearanceConfigurator>();
        return services;
    }
}
