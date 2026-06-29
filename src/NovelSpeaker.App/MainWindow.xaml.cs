using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
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
    private readonly INavigationService _navigationService;
    private readonly INavigationViewPageProvider _pageProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly MainWindowViewModel _viewModel;
    private bool _isNavigationInitialized;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService,
        INavigationViewPageProvider pageProvider,
        IServiceProvider serviceProvider,
        IMainWindowAppearanceConfigurator appearanceConfigurator)
    {
        _appearanceConfigurator = appearanceConfigurator;
        _navigationService = navigationService;
        _pageProvider = pageProvider;
        _serviceProvider = serviceProvider;
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    internal NavigationView NavigationViewControl => RootNavigationView;

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _appearanceConfigurator.Configure(this);
        if (_isNavigationInitialized)
        {
            return;
        }

        RootNavigationView.SetPageProviderService(_pageProvider);
        RootNavigationView.SetServiceProvider(_serviceProvider);
        _navigationService.SetNavigationControl(RootNavigationView);
        _navigationService.Navigate(typeof(LibraryPage));
        _isNavigationInitialized = true;
    }
}
