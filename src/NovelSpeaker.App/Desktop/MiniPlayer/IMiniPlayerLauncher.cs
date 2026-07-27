namespace NovelSpeaker.App.Desktop.MiniPlayer;

/// <summary>
/// Opens the process-owned mini-player through the desktop lifecycle owner.
/// </summary>
public interface IMiniPlayerLauncher
{
    Task OpenMiniPlayerAsync(CancellationToken cancellationToken);
}
