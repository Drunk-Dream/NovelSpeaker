using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell.Activation;

public interface IShellActivationCoordinator : IDisposable
{
    CancellationToken LifetimeToken { get; }

    bool IsCloseApproved { get; }

    bool IsShutdownRequested { get; }

    bool IsPlayerPageActive { get; }

    Task ActivateAsync(ShellHostElements host, double windowWidth);

    Task HandleNavigationRequestAsync(
        NavigatingCancelEventArgs eventArgs,
        CancellationToken cancellationToken);

    void HandleNavigated(EventArgs eventArgs);

    Task RequestCloseAsync(Func<Task> closeWindowAsync);
}
