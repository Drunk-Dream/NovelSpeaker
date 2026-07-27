namespace NovelSpeaker.Application.Desktop.MediaControls;

/// <summary>
/// Isolates process-owned system media controls from platform-specific APIs.
/// </summary>
public interface IMediaControlPlatform
{
    event EventHandler<MediaControlCommand>? CommandReceived;

    Task StartAsync(CancellationToken cancellationToken);

    Task UpdateAsync(MediaControlMetadata metadata, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
