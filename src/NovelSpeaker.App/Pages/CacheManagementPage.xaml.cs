using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class CacheManagementPage : System.Windows.Controls.Page, INavigationAware
{
    private readonly INavigationService _navigationService;

    public CacheManagementPage(INavigationService navigationService)
    {
        _navigationService = navigationService;
        InitializeComponent();
    }

    public Task OnNavigatedToAsync()
    {
        return Task.CompletedTask;
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
