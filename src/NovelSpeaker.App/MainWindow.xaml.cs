using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App;

/// <summary>
/// Hosts the Wpf.Ui shell and renders pages resolved from the app navigation service.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly IMainWindowAppearanceConfigurator _appearanceConfigurator;
    private readonly IAppNavigationService _navigationService;
    private readonly IAppPageResolver _pageResolver;
    private readonly MainWindowViewModel _viewModel;
    private CancellationTokenSource? _navigationCts;

    public MainWindow(
        MainWindowViewModel viewModel,
        IAppNavigationService navigationService,
        IAppPageResolver pageResolver,
        IMainWindowAppearanceConfigurator appearanceConfigurator)
    {
        _appearanceConfigurator = appearanceConfigurator;
        _navigationService = navigationService;
        _pageResolver = pageResolver;
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _appearanceConfigurator.Configure(this);
        _navigationService.CurrentEntryChanged += OnCurrentEntryChanged;
        await RenderCurrentEntryAsync(_navigationService.CurrentEntry);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _navigationService.CurrentEntryChanged -= OnCurrentEntryChanged;
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;
    }

    private async void OnCurrentEntryChanged(object? sender, AppNavigationChangedEventArgs e)
    {
        await RenderCurrentEntryAsync(e.Entry);
    }

    private async Task RenderCurrentEntryAsync(AppNavigationEntry entry)
    {
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = new CancellationTokenSource();

        var page = _pageResolver.Resolve(entry);
        if (page is not System.Windows.UIElement element)
        {
            throw new InvalidOperationException($"Resolved page {page.GetType().FullName} is not a UIElement.");
        }

        RootNavigationView.ReplaceContent(element, entry.Parameter);
        UpdateSelectedPrimaryItem(entry.PrimaryDestination);

        if (page is IAppNavigationPage navigationPage)
        {
            try
            {
                await navigationPage.OnNavigatedToAsync(entry, _navigationCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void PrimaryNavigationItem_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not NavigationViewItem item ||
            !Enum.TryParse<AppPrimaryDestination>(item.Tag?.ToString(), ignoreCase: true, out var destination))
        {
            return;
        }

        _navigationService.NavigateToPrimary(destination);
    }

    private void UpdateSelectedPrimaryItem(AppPrimaryDestination destination)
    {
        LibraryNavigationItem.Deactivate(RootNavigationView);
        SettingsNavigationItem.Deactivate(RootNavigationView);

        switch (destination)
        {
            case AppPrimaryDestination.Library:
                LibraryNavigationItem.Activate(RootNavigationView);
                break;
            case AppPrimaryDestination.Settings:
                SettingsNavigationItem.Activate(RootNavigationView);
                break;
        }
    }
}
