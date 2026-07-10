using NovelSpeaker.App.Input;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly IKeyboardShortcutCoordinator _keyboardShortcutCoordinator;
    private readonly IContentDialogService _contentDialogService;
    private readonly IGuardedNavigationService _guardedNavigationService;
    private readonly INavigationService _navigationService;
    private readonly INavigationViewPageProvider _pageProvider;
    private readonly IShellLayoutController _shellLayoutController;
    private readonly ISnackbarService _snackbarService;
    private readonly IServiceProvider _serviceProvider;
    private readonly MainWindowViewModel _viewModel;
    private NavigationViewItem? _currentPrimaryNavigationItem;
    private bool _isShellInfrastructureConfigured;
    private bool _isNavigationInitialized;
    private bool _isPlayerPageActive;

    public MainWindow(
        MainWindowViewModel viewModel,
        IContentDialogService contentDialogService,
        IGuardedNavigationService guardedNavigationService,
        INavigationService navigationService,
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
        PreviewKeyDown += OnPreviewKeyDown;
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
        RootNavigationView.Navigating += OnRootNavigationViewNavigating;
        RootNavigationView.Navigated += OnRootNavigationViewNavigated;
        _navigationService.Navigate(typeof(LibraryPage));
        ApplyPrimaryNavigationSelection(LibraryNavigationItem);
        _isNavigationInitialized = true;
        _shellLayoutController.UpdateWindowWidth(ActualWidth);
        ApplyPaneState(_shellLayoutController.IsPaneOpen);
    }

    private void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        _shellLayoutController.UpdateWindowWidth(e.NewSize.Width);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var handled = await _keyboardShortcutCoordinator.TryHandleAsync(
            e.Key == Key.System ? e.SystemKey : e.Key,
            Keyboard.Modifiers,
            CreateKeyboardShortcutContext(),
            CancellationToken.None);
        e.Handled = handled;
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
            var navigated = await _guardedNavigationService.NavigateAsync(pageId, CancellationToken.None).ConfigureAwait(true);
            if (!navigated)
            {
                ReapplyPrimaryNavigationSelection();
            }

            return;
        }

        var hierarchyNavigated = await _guardedNavigationService.NavigateWithHierarchyAsync(pageType!, null, CancellationToken.None).ConfigureAwait(true);
        if (hierarchyNavigated)
        {
            TryApplyPrimaryNavigationSelection(pageType);
            return;
        }

        ReapplyPrimaryNavigationSelection();
    }

    private void OnRootNavigationViewNavigated(object sender, EventArgs e)
    {
        var page = e.GetType().GetProperty("Page")?.GetValue(e);
        var pageType = page as Type ?? page?.GetType();
        TryApplyPrimaryNavigationSelection(pageType);
    }

    private void ReapplyPrimaryNavigationSelection()
    {
        ApplyPrimaryNavigationSelection(_currentPrimaryNavigationItem ?? LibraryNavigationItem);
    }

    private void TryApplyPrimaryNavigationSelection(Type? pageType)
    {
        _isPlayerPageActive = pageType == typeof(PlayerPage);
        var primaryItem = ResolvePrimaryNavigationItem(pageType);
        if (primaryItem is null)
        {
            return;
        }

        ApplyPrimaryNavigationSelection(primaryItem);
    }

    private void ApplyPrimaryNavigationSelection(NavigationViewItem primaryItem)
    {
        _currentPrimaryNavigationItem = primaryItem;

        if (!ReferenceEquals(RootNavigationView.SelectedItem, primaryItem))
        {
            typeof(NavigationView)
                .GetProperty(nameof(NavigationView.SelectedItem), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?
                .GetSetMethod(nonPublic: true)?
                .Invoke(RootNavigationView, [primaryItem]);
        }

        LibraryNavigationItem.IsActive = ReferenceEquals(primaryItem, LibraryNavigationItem);
        SettingsNavigationItem.IsActive = ReferenceEquals(primaryItem, SettingsNavigationItem);
        PlaybackNavigationItem.IsActive = false;
    }

    private NavigationViewItem? ResolvePrimaryNavigationItem(Type? pageType)
    {
        if (pageType is null)
        {
            return null;
        }

        if (IsLibraryContext(pageType))
        {
            return LibraryNavigationItem;
        }

        if (IsSettingsContext(pageType))
        {
            return SettingsNavigationItem;
        }

        return null;
    }

    private static bool IsLibraryContext(Type pageType)
    {
        return pageType == typeof(LibraryPage)
            || pageType == typeof(BookDetailsPage)
            || pageType == typeof(PlayerPage);
    }

    private static bool IsSettingsContext(Type pageType)
    {
        return pageType == typeof(SettingsPage)
            || pageType == typeof(PlaybackSettingsPage)
            || pageType == typeof(TtsRulesPage)
            || pageType == typeof(ImportTextSettingsPage)
            || pageType == typeof(RegexReplacementRulesPage)
            || pageType == typeof(ChapterRulesPage)
            || pageType == typeof(CacheAndDataPage)
            || pageType == typeof(CacheManagementPage)
            || pageType == typeof(AppearanceSettingsPage)
            || pageType == typeof(DiagnosticsAboutPage);
    }
}
