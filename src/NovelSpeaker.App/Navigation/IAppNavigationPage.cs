namespace NovelSpeaker.App.Navigation;

internal interface IAppNavigationPage
{
    Task OnNavigatedToAsync(AppNavigationEntry entry, CancellationToken cancellationToken);
}
