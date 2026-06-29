namespace NovelSpeaker.App.Navigation;

public sealed class AppNavigationService : IAppNavigationService
{
    private readonly Stack<AppNavigationEntry> _backStack = [];

    public AppNavigationEntry CurrentEntry { get; private set; } = AppNavigationEntry.CreatePrimary(AppPrimaryDestination.Library);

    public bool CanGoBack => _backStack.Count > 0;

    public event EventHandler<AppNavigationChangedEventArgs>? CurrentEntryChanged;

    public bool NavigateToPrimary(AppPrimaryDestination destination)
    {
        return NavigateTo(AppNavigationEntry.CreatePrimary(destination));
    }

    public bool NavigateToSettings(SettingsSection section)
    {
        return NavigateTo(AppNavigationEntry.CreateSettings(section));
    }

    public bool NavigateToPlayer(PlayerNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return NavigateTo(new AppNavigationEntry(AppPageKind.Player, AppPrimaryDestination.Library, Parameter: request));
    }

    public bool NavigateToBookDetails(BookDetailsNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return NavigateTo(new AppNavigationEntry(AppPageKind.BookDetails, AppPrimaryDestination.Library, Parameter: request));
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        CurrentEntry = _backStack.Pop();
        RaiseCurrentEntryChanged();
        return true;
    }

    private bool NavigateTo(AppNavigationEntry target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (CurrentEntry == target)
        {
            return false;
        }

        _backStack.Push(CurrentEntry);
        CurrentEntry = target;
        RaiseCurrentEntryChanged();
        return true;
    }

    private void RaiseCurrentEntryChanged()
    {
        CurrentEntryChanged?.Invoke(this, new AppNavigationChangedEventArgs(CurrentEntry));
    }
}
