namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Rebuilds the active playback chapter after regex replacement settings change.
/// </summary>
public interface IPlaybackRegexReplacementRefresher
{
    Task RefreshRegexReplacementAsync(CancellationToken cancellationToken);
}
