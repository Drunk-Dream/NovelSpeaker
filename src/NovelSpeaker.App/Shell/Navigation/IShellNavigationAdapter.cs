using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell.Navigation;

public interface IShellNavigationAdapter : IAppNavigator
{
    bool IsBypassingGuard { get; }

    bool IsPlayerPageActive { get; }

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
