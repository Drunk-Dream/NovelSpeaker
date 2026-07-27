namespace NovelSpeaker.App.Desktop.MiniPlayer;

public interface IMiniPlayerPlacementPersistence
{
    Task FlushPlacementAsync(CancellationToken cancellationToken);
}
