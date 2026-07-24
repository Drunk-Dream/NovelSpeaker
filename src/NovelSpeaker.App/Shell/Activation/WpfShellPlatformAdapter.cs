using System.Windows.Controls;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell.Activation;

public sealed class WpfShellPlatformAdapter : IShellPlatformAdapter
{
    private readonly IMainWindowAppearanceConfigurator _appearanceConfigurator;
    private readonly IContentDialogService _contentDialogService;
    private readonly IShellNavigationAdapter _navigationAdapter;
    private readonly INavigationViewPageProvider _pageProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISnackbarService _snackbarService;

    public WpfShellPlatformAdapter(
        IMainWindowAppearanceConfigurator appearanceConfigurator,
        IContentDialogService contentDialogService,
        IShellNavigationAdapter navigationAdapter,
        INavigationViewPageProvider pageProvider,
        IServiceProvider serviceProvider,
        ISnackbarService snackbarService)
    {
        _appearanceConfigurator = appearanceConfigurator;
        _contentDialogService = contentDialogService;
        _navigationAdapter = navigationAdapter;
        _pageProvider = pageProvider;
        _serviceProvider = serviceProvider;
        _snackbarService = snackbarService;
    }

    public void ConfigureInfrastructure(ShellHostElements host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _contentDialogService.SetDialogHost(host.ContentDialogHost);
        _snackbarService.SetSnackbarPresenter(host.SnackbarPresenter);
        _appearanceConfigurator.Configure(host.Window);
    }

    public void InitializeNavigation(ShellHostElements host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.NavigationView.SetPageProviderService(_pageProvider);
        host.NavigationView.SetServiceProvider(_serviceProvider);
        _navigationAdapter.Initialize(
            host.NavigationView,
            host.LibraryItem,
            host.SettingsItem,
            host.PlaybackItem);
    }

    public void ConfigureNavigationPresenter(ShellHostElements host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.NavigationView.ApplyTemplate();
        if (host.NavigationView.Template?.FindName(
                "PART_NavigationViewContentPresenter",
                host.NavigationView) is not NavigationViewContentPresenter presenter)
        {
            return;
        }

        presenter.SetValue(
            NavigationViewContentPresenter.IsDynamicScrollViewerEnabledProperty,
            false);

        if (presenter.TryFindResource(
                "DefaultNavigationViewContentPresenterControlTemplate") is ControlTemplate template &&
            !ReferenceEquals(presenter.Template, template))
        {
            presenter.Template = template;
            presenter.ApplyTemplate();
        }
    }
}
