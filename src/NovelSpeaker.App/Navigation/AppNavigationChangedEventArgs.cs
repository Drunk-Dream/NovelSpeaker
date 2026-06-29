namespace NovelSpeaker.App.Navigation;

public sealed class AppNavigationChangedEventArgs : EventArgs
{
    public AppNavigationChangedEventArgs(AppNavigationEntry entry)
    {
        Entry = entry;
    }

    public AppNavigationEntry Entry { get; }
}
