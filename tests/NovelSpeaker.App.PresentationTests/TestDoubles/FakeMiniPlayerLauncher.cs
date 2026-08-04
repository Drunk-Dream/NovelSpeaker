using NovelSpeaker.App.Desktop.MiniPlayer;

namespace NovelSpeaker.App.PresentationTests.TestDoubles;

internal sealed class FakeMiniPlayerLauncher : IMiniPlayerLauncher
{
    public int OpenCount { get; private set; }

    public Task OpenMiniPlayerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenCount++;
        return Task.CompletedTask;
    }
}
