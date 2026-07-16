using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Settings;

public sealed class AppSettingsChangedEventArgs : EventArgs
{
    public AppSettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
        Previous = previous;
        Current = current;
    }

    public AppSettings Previous { get; }

    public AppSettings Current { get; }
}
