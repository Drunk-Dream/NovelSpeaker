using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App;

/// <summary>
/// Hosts the Wpf.Ui shell and wires the official navigation services to the root navigation view.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly IMainWindowAppearanceConfigurator _appearanceConfigurator;
    private readonly IContentDialogService _contentDialogService;
    private readonly IGuardedNavigationService _guardedNavigationService;
    private readonly INavigationService _navigationService;
    private readonly INavigationViewPageProvider _pageProvider;
    private readonly IShellLayoutController _shellLayoutController;
    private readonly ISnackbarService _snackbarService;
    private readonly IServiceProvider _serviceProvider;
    private readonly MainWindowViewModel _viewModel;
    private bool _isShellInfrastructureConfigured;
    private bool _isNavigationInitialized;

    public MainWindow(
        MainWindowViewModel viewModel,
        IContentDialogService contentDialogService,
        IGuardedNavigationService guardedNavigationService,
        INavigationService navigationService,
        INavigationViewPageProvider pageProvider,
        ISnackbarService snackbarService,
        IServiceProvider serviceProvider,
        IMainWindowAppearanceConfigurator appearanceConfigurator,
        IShellLayoutController shellLayoutController)
    {
        _appearanceConfigurator = appearanceConfigurator;
        _contentDialogService = contentDialogService;
        _guardedNavigationService = guardedNavigationService;
        _navigationService = navigationService;
        _pageProvider = pageProvider;
        _shellLayoutController = shellLayoutController;
        _snackbarService = snackbarService;
        _serviceProvider = serviceProvider;
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        RootNavigationView.PaneOpened += OnPaneOpened;
        RootNavigationView.PaneClosed += OnPaneClosed;
        _shellLayoutController.PaneStateChanged += OnPaneStateChanged;
    }

    internal NavigationView NavigationViewControl => RootNavigationView;

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_isShellInfrastructureConfigured)
        {
            _contentDialogService.SetDialogHost(RootContentDialogHost);
            _snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);
            _appearanceConfigurator.Configure(this);
            _isShellInfrastructureConfigured = true;
        }

        if (_isNavigationInitialized)
        {
            ConfigureNavigationContentPresenter();
            _shellLayoutController.UpdateWindowWidth(ActualWidth);
            return;
        }

        RootNavigationView.SetPageProviderService(_pageProvider);
        RootNavigationView.SetServiceProvider(_serviceProvider);
        _navigationService.SetNavigationControl(RootNavigationView);
        ConfigureNavigationContentPresenter();
        _navigationService.Navigate(typeof(LibraryPage));
        RootNavigationView.Navigating += OnRootNavigationViewNavigating;
        _isNavigationInitialized = true;
        _shellLayoutController.UpdateWindowWidth(ActualWidth);
        ApplyPaneState(_shellLayoutController.IsPaneOpen);
    }

    private void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        _shellLayoutController.UpdateWindowWidth(e.NewSize.Width);
    }

    private void OnPaneOpened(object sender, System.Windows.RoutedEventArgs e)
    {
        _shellLayoutController.HandlePaneStateChanged(true);
    }

    private void OnPaneClosed(object sender, System.Windows.RoutedEventArgs e)
    {
        _shellLayoutController.HandlePaneStateChanged(false);
    }

    private void OnPaneStateChanged(object? sender, bool isPaneOpen)
    {
        ApplyPaneState(isPaneOpen);
    }

    private void ApplyPaneState(bool isPaneOpen)
    {
        if (RootNavigationView.IsPaneOpen == isPaneOpen)
        {
            return;
        }

        RootNavigationView.IsPaneOpen = isPaneOpen;
    }

    private void ConfigureNavigationContentPresenter()
    {
        RootNavigationView.ApplyTemplate();

        if (RootNavigationView.Template?.FindName("PART_NavigationViewContentPresenter", RootNavigationView) is not NavigationViewContentPresenter presenter)
        {
            return;
        }

        presenter.SetValue(NavigationViewContentPresenter.IsDynamicScrollViewerEnabledProperty, false);

        if (presenter.TryFindResource("DefaultNavigationViewContentPresenterControlTemplate") is ControlTemplate template &&
            !ReferenceEquals(presenter.Template, template))
        {
            presenter.Template = template;
            presenter.ApplyTemplate();
        }
    }

    private async void PlaybackNavigationItem_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel.NavigateToNowPlayingCommand.CanExecute(null))
        {
            await _viewModel.NavigateToNowPlayingCommand.ExecuteAsync(null);
        }
    }

    private async void OnRootNavigationViewNavigating(object sender, Wpf.Ui.Controls.NavigatingCancelEventArgs e)
    {
        if (_guardedNavigationService.IsBypassingGuard)
        {
            return;
        }

        var pageId = e.GetType().GetProperty("PageId")?.GetValue(e) as string;
        var page = e.GetType().GetProperty("Page")?.GetValue(e);
        var pageType = page as Type ?? page?.GetType();
        if (string.IsNullOrWhiteSpace(pageId) && pageType is null)
        {
            return;
        }

        e.Cancel = true;
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            await _guardedNavigationService.NavigateAsync(pageId, CancellationToken.None).ConfigureAwait(true);
            return;
        }

        await _guardedNavigationService.NavigateWithHierarchyAsync(pageType!, null, CancellationToken.None).ConfigureAwait(true);
    }
}
