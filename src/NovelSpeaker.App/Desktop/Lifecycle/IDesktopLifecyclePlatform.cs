namespace NovelSpeaker.App.Desktop.Lifecycle;

public interface IDesktopLifecyclePlatform
{
    event EventHandler<DesktopLifecycleCommand>? CommandReceived;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task ShowMainWindowAsync(CancellationToken cancellationToken);

    Task HideMainWindowAsync(CancellationToken cancellationToken);

    Task ShowMiniPlayerAsync(CancellationToken cancellationToken);

    Task HideMiniPlayerAsync(CancellationToken cancellationToken);

    Task CloseMainWindowAsync(CancellationToken cancellationToken);

    Task<DesktopCloseChoice> PromptForCloseChoiceAsync(CancellationToken cancellationToken);
}
