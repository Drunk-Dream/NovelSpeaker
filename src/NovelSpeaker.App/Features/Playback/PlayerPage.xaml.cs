using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Presentation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Playback;

public partial class PlayerPage : System.Windows.Controls.Page, INavigationAware, INavigableView<PlayerViewModel>
{
    private readonly PageActivationController _activation = new();

    public PlayerPage(PlayerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PlayerView.DataContext = ViewModel;
    }

    public PlayerViewModel ViewModel { get; }

    public PlayerRoute? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        ViewModel.OnPageNavigatedTo();
        activation.Register(ViewModel.OnPageNavigatedFrom);
        LastRequest = DataContext as PlayerRoute;

        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            if (LastRequest is not null && activation.IsCurrent)
            {
                await ViewModel.HandleNavigationAsync(LastRequest, activation.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }
}
