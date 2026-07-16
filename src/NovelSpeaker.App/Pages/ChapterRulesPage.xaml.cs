using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using System.Windows;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class ChapterRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<ChapterRulesViewModel>
{
    private readonly INavigationGuardService _navigationGuardService;
    private IDisposable? _guardRegistration;
    private bool _hasLoaded;

    public ChapterRulesPage(
        ChapterRulesViewModel viewModel,
        INavigationGuardService navigationGuardService)
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        InitializeComponent();
        ChapterRulesView.DataContext = ViewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ChapterRulesViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        _guardRegistration?.Dispose();
        _guardRegistration = _navigationGuardService.Register(ViewModel.ConfirmLeaveAsync);

        if (_hasLoaded)
        {
            return;
        }

        await ViewModel.LoadAsync(CancellationToken.None);
        _hasLoaded = true;
    }

    public Task OnNavigatedFromAsync()
    {
        UnregisterNavigationGuard();
        ViewModel.HandleNavigatedFrom();
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
