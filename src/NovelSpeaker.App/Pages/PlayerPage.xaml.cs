using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Pages;

public partial class PlayerPage : System.Windows.Controls.Page, IAppNavigationPage
{
    private readonly IAppNavigationService _navigationService;
    private readonly PlayerViewModel _viewModel;
    private bool _hasLoaded;

    public PlayerPage(IAppNavigationService navigationService, PlayerViewModel viewModel)
    {
        _navigationService = navigationService;
        _viewModel = viewModel;
        InitializeComponent();
        PlayerView.DataContext = viewModel;
    }

    public PlayerNavigationRequest? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync(AppNavigationEntry entry, CancellationToken cancellationToken)
    {
        LastRequest = entry.Parameter as PlayerNavigationRequest;

        if (_hasLoaded)
        {
            return;
        }

        await _viewModel.LoadAsync(cancellationToken);
        _hasLoaded = true;
    }

    private void BackButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _navigationService.GoBack();
    }
}
