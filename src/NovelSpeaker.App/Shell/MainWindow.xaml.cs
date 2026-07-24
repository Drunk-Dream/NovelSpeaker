using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shared.Theming;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell;

/// <summary>
/// Hosts the Wpf.Ui shell and wires the official navigation services to the root navigation view.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly IMainWindowAppearanceConfigurator _appearanceConfigurator;
    private readonly IKeyboardShortcutCoordinator _keyboardShortcutCoordinator;
    private readonly IContentDialogService _contentDialogService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly INavigationGuardService _navigationGuardService;
    private readonly IShellNavigationAdapter _navigationAdapter;
    private readonly INavigationViewPageProvider _pageProvider;
    private readonly IShellLayoutController _shellLayoutController;
    private readonly ISnackbarService _snackbarService;
    private readonly IServiceProvider _serviceProvider;
    private readonly MainWindowViewModel _viewModel;
    private readonly CancellationTokenSource _windowLifetimeCancellation = new();
    private bool _isShellInfrastructureConfigured;
    private bool _isNavigationInitialized;
    private bool _isPlayerPageActive;
    private bool _isCloseConfirmationInProgress;
    private bool _isCloseApproved;

    public MainWindow(
        MainWindowViewModel viewModel,
        IContentDialogService contentDialogService,
        IAppFeedbackService feedbackService,
        INavigationGuardService navigationGuardService,
        IShellNavigationAdapter navigationAdapter,
        INavigationViewPageProvider pageProvider,
        ISnackbarService snackbarService,
        IServiceProvider serviceProvider,
        IMainWindowAppearanceConfigurator appearanceConfigurator,
        IShellLayoutController shellLayoutController,
        IKeyboardShortcutCoordinator keyboardShortcutCoordinator)
    {
        _appearanceConfigurator = appearanceConfigurator;
        _keyboardShortcutCoordinator = keyboardShortcutCoordinator;
        _contentDialogService = contentDialogService;
        _feedbackService = feedbackService;
        _navigationGuardService = navigationGuardService;
        _navigationAdapter = navigationAdapter;
        _pageProvider = pageProvider;
        _shellLayoutController = shellLayoutController;
        _snackbarService = snackbarService;
        _serviceProvider = serviceProvider;
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Closing += OnClosing;
        SizeChanged += OnSizeChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        RootNavigationView.PaneOpened += OnPaneOpened;
        RootNavigationView.PaneClosed += OnPaneClosed;
        _shellLayoutController.PaneStateChanged += OnPaneStateChanged;
    }

    internal NavigationView NavigationViewControl => RootNavigationView;

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isCloseApproved)
        {
            return;
        }

        e.Cancel = true;
        if (_isCloseConfirmationInProgress)
        {
            return;
        }

        _isCloseConfirmationInProgress = true;
        try
        {
            if (!await _navigationGuardService.ConfirmNavigationAsync(_windowLifetimeCancellation.Token).ConfigureAwait(true))
            {
                return;
            }

            _isCloseApproved = true;
            await Dispatcher.InvokeAsync(Close);
        }
        catch (OperationCanceledException)
        {
            _isCloseApproved = false;
            // Window-close cancellation is normal control flow; the window remains open.
        }
        catch (Exception exception)
        {
            _isCloseApproved = false;
            _feedbackService.ShowProjectedNotification("关闭应用失败", _feedbackService.Project(exception));
        }
        finally
        {
            _isCloseConfirmationInProgress = false;
        }
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
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
            _navigationAdapter.Initialize(
                RootNavigationView,
                LibraryNavigationItem,
                SettingsNavigationItem,
                PlaybackNavigationItem);
            ConfigureNavigationContentPresenter();
            RootNavigationView.Navigating += OnRootNavigationViewNavigating;
            RootNavigationView.Navigated += OnRootNavigationViewNavigated;
            _isNavigationInitialized = true;
            await _navigationAdapter.NavigateAsync(
                AppRoutes.Library,
                _windowLifetimeCancellation.Token,
                bypassGuard: true).ConfigureAwait(true);
            _shellLayoutController.UpdateWindowWidth(ActualWidth);
            ApplyPaneState(_shellLayoutController.IsPaneOpen);
        }
        catch (OperationCanceledException) when (_windowLifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("打开页面失败", _feedbackService.Project(exception));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _windowLifetimeCancellation.Cancel();
        _windowLifetimeCancellation.Dispose();
    }

    private void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        _shellLayoutController.UpdateWindowWidth(e.NewSize.Width);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Handled)
            {
                return;
            }

            var handled = await _keyboardShortcutCoordinator.TryHandleAsync(
                e.Key == Key.System ? e.SystemKey : e.Key,
                Keyboard.Modifiers,
                CreateKeyboardShortcutContext(),
                _windowLifetimeCancellation.Token);
            e.Handled = handled;
        }
        catch (OperationCanceledException) when (_windowLifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("快捷键操作失败", _feedbackService.Project(exception));
        }
    }

    private KeyboardShortcutContext CreateKeyboardShortcutContext()
    {
        var focusedElement = Keyboard.FocusedElement as DependencyObject;
        return new KeyboardShortcutContext(
            _isPlayerPageActive,
            IsTextEditingElement(focusedElement),
            IsTransientUiOpen(focusedElement) || HasOpenContentDialog());
    }

    private static bool IsTextEditingElement(DependencyObject? element)
    {
        for (var current = element; current is not null; current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current))
        {
            if (current is TextBoxBase or System.Windows.Controls.PasswordBox || current is ComboBox { IsEditable: true })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransientUiOpen(DependencyObject? focusedElement)
    {
        for (var current = focusedElement; current is not null; current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current))
        {
            if (current is ComboBox { IsDropDownOpen: true } ||
                current is ContextMenu { IsOpen: true } ||
                current is System.Windows.Controls.MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasOpenContentDialog()
    {
        return FindVisibleContentDialog(RootContentDialogHost) is not null;
    }

    private static ContentDialog? FindVisibleContentDialog(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ContentDialog dialog && dialog.IsVisible)
            {
                return dialog;
            }

            var nested = FindVisibleContentDialog(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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
        try
        {
            if (_viewModel.NavigateToNowPlayingCommand.CanExecute(null))
            {
                await _viewModel.NavigateToNowPlayingCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException) when (_windowLifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("打开正在播放失败", _feedbackService.Project(exception));
        }
    }

    private async void OnRootNavigationViewNavigating(object sender, Wpf.Ui.Controls.NavigatingCancelEventArgs e)
    {
        try
        {
            if (_navigationAdapter.IsBypassingGuard)
            {
                return;
            }

            e.Cancel = true;
            await _navigationAdapter.NavigateFromShellAsync(
                e,
                _windowLifetimeCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_windowLifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("页面导航失败", _feedbackService.Project(exception));
        }
    }

    private void OnRootNavigationViewNavigated(object sender, EventArgs e)
    {
        _navigationAdapter.SynchronizeSelection(e);
        _isPlayerPageActive = _navigationAdapter.CurrentRouteId == AppRouteId.Player;
    }
}
