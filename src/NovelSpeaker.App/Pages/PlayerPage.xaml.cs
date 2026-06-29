using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class PlayerPage : System.Windows.Controls.Page, INavigationAware, INavigableView<PlayerViewModel>
{
    private readonly INavigationService _navigationService;
    private bool _hasLoaded;

    public PlayerPage(INavigationService navigationService, PlayerViewModel viewModel)
    {
        _navigationService = navigationService;
        ViewModel = viewModel;
        InitializeComponent();
        PlayerView.DataContext = ViewModel;
    }

    public PlayerViewModel ViewModel { get; }

    public PlayerNavigationRequest? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync()
    {
        LastRequest = DataContext as PlayerNavigationRequest;

        if (_hasLoaded)
        {
            return;
        }

        await ViewModel.LoadAsync(CancellationToken.None);
        _hasLoaded = true;
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    private void BackButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = _navigationService.GoBack();
    }
}
