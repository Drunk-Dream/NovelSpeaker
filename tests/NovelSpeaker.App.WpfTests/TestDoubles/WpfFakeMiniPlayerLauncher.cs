using NovelSpeaker.App.Desktop.MiniPlayer;

namespace NovelSpeaker.App.WpfTests.TestDoubles;

internal sealed class WpfFakeMiniPlayerLauncher : IMiniPlayerLauncher
{
    public int OpenCount { get; private set; }

    public Task OpenMiniPlayerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenCount++;
        return Task.CompletedTask;
    }
}
