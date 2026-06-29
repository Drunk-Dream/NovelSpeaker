namespace NovelSpeaker.App.Navigation;

public interface IAppNavigationService
{
    AppNavigationEntry CurrentEntry { get; }

    bool CanGoBack { get; }

    event EventHandler<AppNavigationChangedEventArgs>? CurrentEntryChanged;

    bool NavigateToPrimary(AppPrimaryDestination destination);

    bool NavigateToSettings(SettingsSection section);

    bool NavigateToPlayer(PlayerNavigationRequest request);

    bool NavigateToBookDetails(BookDetailsNavigationRequest request);

    bool GoBack();
}
