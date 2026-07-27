namespace NovelSpeaker.App.Desktop.Lifecycle;

public interface IDesktopLifecycleCoordinator
{
    bool IsExitApproved { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task RequestMainWindowCloseAsync(CancellationToken cancellationToken);

    Task RequestExitAsync(CancellationToken cancellationToken);
}
