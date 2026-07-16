using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using System.Windows;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class RegexReplacementRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<RegexReplacementRulesViewModel>
{
    private readonly INavigationGuardService _navigationGuardService;
    private IDisposable? _guardRegistration;

    public RegexReplacementRulesPage(
        RegexReplacementRulesViewModel viewModel,
        INavigationGuardService navigationGuardService)
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        InitializeComponent();
        Workspace.DataContext = viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public RegexReplacementRulesViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        _guardRegistration?.Dispose();
        _guardRegistration = _navigationGuardService.Register(ViewModel.ConfirmLeaveAsync);
        return ViewModel.LoadAsync(CancellationToken.None);
    }

    public Task OnNavigatedFromAsync()
    {
        UnregisterNavigationGuard();
        return Task.CompletedTask;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnregisterNavigationGuard();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _guardRegistration ??= _navigationGuardService.Register(ViewModel.ConfirmLeaveAsync);
    }

    private void UnregisterNavigationGuard()
    {
        _guardRegistration?.Dispose();
        _guardRegistration = null;
    }
}
