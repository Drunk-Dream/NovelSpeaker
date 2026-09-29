using NovelSpeaker.Domain.Speech.Providers;
namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents the current UI-facing view of the active book playback session.
/// </summary>
public sealed record PlaybackSnapshot(
    PlaybackState State,
    string? BookId,
    string? BookTitle,
    int ChapterIndex,
    string? ChapterTitle,
    int SegmentIndex,
    int SegmentCount,
    ProviderId? ProviderId,
    string? ProviderName,
    int SpeakSpeed,
    long PositionMilliseconds,
    long DurationMilliseconds,
    string? Message,
    bool IsUsingCache,
    bool CanRetry,
    string? BookAuthor = null,
    bool HasAvailableProvider = true,
    long ContentRevision = 0,
    double Volume = PlaybackVolume.Default,
    bool HasLoadedAudio = false)
{
    public static PlaybackSnapshot Idle { get; } = new(
        PlaybackState.Idle,
        null,
        null,
        0,
        null,
        0,
        0,
        null,
        null,
        NovelSpeaker.Domain.Settings.AppSettings.DefaultSpeakSpeedValue,
        0,
        0,
        "请选择一本书并开始播放。",
        false,
        false,
        null,
        true);
}
