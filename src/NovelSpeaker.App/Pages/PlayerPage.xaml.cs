using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class PlayerPage : System.Windows.Controls.Page, INavigationAware, INavigableView<PlayerViewModel>
{
    public PlayerPage(PlayerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PlayerView.DataContext = ViewModel;
    }

    public PlayerViewModel ViewModel { get; }

    public PlayerNavigationRequest? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync()
    {
        LastRequest = DataContext as PlayerNavigationRequest;

        await ViewModel.LoadAsync(CancellationToken.None);

        if (LastRequest is not null)
        {
            await ViewModel.HandleNavigationAsync(LastRequest, CancellationToken.None);
        }
    }

    public Task OnNavigatedFromAsync()
    {
        ViewModel.OnPageNavigatedFrom();
        return Task.CompletedTask;
    }
}
