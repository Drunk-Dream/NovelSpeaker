using NovelSpeaker.App.Navigation;

namespace NovelSpeaker.App.Pages;

public partial class CacheManagementPage : System.Windows.Controls.Page, IAppNavigationPage
{
    private readonly IAppNavigationService _navigationService;

    public CacheManagementPage(IAppNavigationService navigationService)
    {
        _navigationService = navigationService;
        InitializeComponent();
    }

    public Task OnNavigatedToAsync(AppNavigationEntry entry, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void BackButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _navigationService.GoBack();
    }
}
