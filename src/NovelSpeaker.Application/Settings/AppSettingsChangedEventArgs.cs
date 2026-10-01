using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Settings;

public sealed class AppSettingsChangedEventArgs : EventArgs
{
    public AppSettingsChangedEventArgs(AppSettings previous, AppSettings current, bool isSnapshotReplacement = false)
    {
        Previous = previous;
        Current = current;
        IsSnapshotReplacement = isSnapshotReplacement;
    }

    public AppSettings Previous { get; }

    public AppSettings Current { get; }

    public bool IsSnapshotReplacement { get; }
}
