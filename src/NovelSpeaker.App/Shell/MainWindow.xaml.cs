using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Input;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell;

/// <summary>
/// Connects WPF window and control events to the shell coordinators and visual adapters.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly IShellActivationCoordinator _activationCoordinator;
    private readonly IKeyboardShortcutCoordinator _keyboardShortcutCoordinator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IShortcutContextResolver _shortcutContextResolver;
    private readonly IShellLayoutController _shellLayoutController;
    private readonly MainWindowViewModel _viewModel;
    private Func<CancellationToken, Task>? _requestCloseAsync;
    private Func<bool>? _isExitApproved;
    private bool _navigationEventsConnected;

    public MainWindow(
        MainWindowViewModel viewModel,
        IAppFeedbackService feedbackService,
        IShellActivationCoordinator activationCoordinator,
        IShellLayoutController shellLayoutController,
        IKeyboardShortcutCoordinator keyboardShortcutCoordinator,
        IShortcutContextResolver shortcutContextResolver)
    {
        _activationCoordinator = activationCoordinator;
        _keyboardShortcutCoordinator = keyboardShortcutCoordinator;
        _feedbackService = feedbackService;
        _shortcutContextResolver = shortcutContextResolver;
        _shellLayoutController = shellLayoutController;
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

    internal void ConfigureDesktopLifecycle(
        Func<CancellationToken, Task> requestCloseAsync,
        Func<bool> isExitApproved)
    {
        _requestCloseAsync = requestCloseAsync ?? throw new ArgumentNullException(nameof(requestCloseAsync));
        _isExitApproved = isExitApproved ?? throw new ArgumentNullException(nameof(isExitApproved));
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExitApproved?.Invoke() == true)
        {
            return;
        }

        e.Cancel = true;
        try
        {
            var requestCloseAsync = _requestCloseAsync
                ?? throw new InvalidOperationException("桌面生命周期回调尚未配置。");
            await requestCloseAsync(_activationCoordinator.LifetimeToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (
            _activationCoordinator.LifetimeToken.IsCancellationRequested ||
            _activationCoordinator.IsShutdownRequested)
        {
            // Window-close cancellation is normal control flow; the window remains open.
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("关闭应用失败", _feedbackService.Project(exception));
        }
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            if (!_navigationEventsConnected)
            {
                RootNavigationView.Navigating += OnRootNavigationViewNavigating;
                RootNavigationView.Navigated += OnRootNavigationViewNavigated;
                _navigationEventsConnected = true;
            }

            await _activationCoordinator.ActivateAsync(
                CreateShellHostElements(),
                ActualWidth).ConfigureAwait(true);
            ApplyPaneState(_shellLayoutController.IsPaneOpen);
        }
        catch (OperationCanceledException) when (
            _activationCoordinator.LifetimeToken.IsCancellationRequested ||
            _activationCoordinator.IsShutdownRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("打开页面失败", _feedbackService.Project(exception));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _activationCoordinator.Dispose();
    }

    private void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        _shellLayoutController.UpdateWindowWidth(e.NewSize.Width);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (_activationCoordinator.IsShutdownRequested)
            {
                return;
            }

            if (e.Handled)
            {
                return;
            }

            var handled = await _keyboardShortcutCoordinator.TryHandleAsync(
                e.Key == Key.System ? e.SystemKey : e.Key,
                Keyboard.Modifiers,
                _shortcutContextResolver.Resolve(
                    _activationCoordinator.IsPlayerPageActive,
                    Keyboard.FocusedElement as DependencyObject,
                    RootContentDialogHost),
                _activationCoordinator.LifetimeToken);
            e.Handled = handled;
        }
        catch (OperationCanceledException) when (
            _activationCoordinator.LifetimeToken.IsCancellationRequested ||
            _activationCoordinator.IsShutdownRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("快捷键操作失败", _feedbackService.Project(exception));
        }
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

    private async void PlaybackNavigationItem_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            if (_activationCoordinator.IsShutdownRequested)
            {
                return;
            }

            if (_viewModel.NavigateToNowPlayingCommand.CanExecute(null))
            {
                await _viewModel.NavigateToNowPlayingCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException) when (
            _activationCoordinator.LifetimeToken.IsCancellationRequested ||
            _activationCoordinator.IsShutdownRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("打开正在播放失败", _feedbackService.Project(exception));
        }
    }

    private void ActiveCacheNavigationItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activationCoordinator.IsShutdownRequested)
        {
            return;
        }

        if (_viewModel.ActiveCache.ToggleFlyoutCommand.CanExecute(null))
        {
            _viewModel.ActiveCache.ToggleFlyoutCommand.Execute(null);
        }
    }

    private void ChapterExportNavigationItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activationCoordinator.IsShutdownRequested)
        {
            return;
        }

        if (_viewModel.ChapterExport.ToggleFlyoutCommand.CanExecute(null))
        {
            _viewModel.ChapterExport.ToggleFlyoutCommand.Execute(null);
        }
    }

    private async void OnRootNavigationViewNavigating(object sender, Wpf.Ui.Controls.NavigatingCancelEventArgs e)
    {
        try
        {
            await _activationCoordinator.HandleNavigationRequestAsync(
                e,
                _activationCoordinator.LifetimeToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (
            _activationCoordinator.LifetimeToken.IsCancellationRequested ||
            _activationCoordinator.IsShutdownRequested)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("页面导航失败", _feedbackService.Project(exception));
        }
    }

    private void OnRootNavigationViewNavigated(object sender, EventArgs e)
    {
        _activationCoordinator.HandleNavigated(e);
    }

    private ShellHostElements CreateShellHostElements()
    {
        return new ShellHostElements(
            this,
            RootNavigationView,
            LibraryNavigationItem,
            SettingsNavigationItem,
            PlaybackNavigationItem,
            RootContentDialogHost,
            RootSnackbarPresenter);
    }
}
