namespace NovelSpeaker.Application.Desktop.MediaControls;

public interface IMediaControlCoordinator
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
