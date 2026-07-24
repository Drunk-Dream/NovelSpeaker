using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Navigation;

public interface IShellNavigationAdapter : IAppNavigator
{
    bool IsBypassingGuard { get; }

    AppRouteId CurrentRouteId { get; }

    void Initialize(
        INavigationView navigationView,
        NavigationViewItem libraryItem,
        NavigationViewItem settingsItem,
        NavigationViewItem playbackItem);

    Task<bool> NavigateFromShellAsync(
        NavigatingCancelEventArgs eventArgs,
        CancellationToken cancellationToken);

    void SynchronizeSelection(EventArgs eventArgs);
}
