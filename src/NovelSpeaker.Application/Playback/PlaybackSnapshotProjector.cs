using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Immutable input for creating one playback snapshot.
/// </summary>
internal sealed record PlaybackSnapshotProjectionInput(
    PlaybackState State,
    PlaybackBookContent Book,
    int ChapterIndex,
    int SegmentIndex,
    SelectedPlaybackRule? SelectedRule,
    int SpeakSpeed,
    long PositionMilliseconds,
    long DurationMilliseconds,
    string? Message,
    bool IsUsingCache,
    bool CanRetry,
    long ContentRevision = 0,
    int? SegmentCountOverride = null,
    double Volume = PlaybackVolume.Default);

/// <summary>
/// Pure projection from explicit playback data to an immutable UI snapshot.
/// It owns no coordinator state and never publishes events.
/// </summary>
internal static class PlaybackSnapshotProjector
{
    internal static PlaybackSnapshot Project(PlaybackSnapshotProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var chapter = input.Book.Chapters.FirstOrDefault(
            candidate => candidate.ChapterIndex == input.ChapterIndex);
        return new PlaybackSnapshot(
            input.State,
            input.Book.BookId,
            input.Book.BookTitle,
            input.ChapterIndex,
            chapter?.Title,
            input.SegmentIndex,
            input.SegmentCountOverride ?? chapter?.Segments.Count ?? 0,
            input.SelectedRule?.RuleId,
            input.SelectedRule?.RuleName,
            AppSettings.NormalizeSpeakSpeed(input.SpeakSpeed),
            input.PositionMilliseconds,
            input.DurationMilliseconds,
            input.Message,
            input.IsUsingCache,
            input.CanRetry,
            input.Book.BookAuthor,
            input.SelectedRule is not null,
            input.ContentRevision,
            PlaybackVolume.Normalize(input.Volume));
    }
}
