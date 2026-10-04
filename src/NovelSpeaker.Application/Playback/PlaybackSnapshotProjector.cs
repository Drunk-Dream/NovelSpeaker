namespace NovelSpeaker.Application.Playback;

/// <summary>Pure projection of one authoritative read; volume remains a process concern.</summary>
internal static class PlaybackSnapshotProjector
{
    internal static PlaybackSnapshot Project(PlaybackRuntimeState state, double volume = PlaybackVolume.Default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var chapter = state.Position is { } position
            ? state.Book?.Chapters.FirstOrDefault(c => c.ChapterIndex == position.ChapterIndex)
            : null;
        return new PlaybackSnapshot(
            state.State,
            state.Book?.BookId,
            state.Book?.BookTitle,
            state.Position?.ChapterIndex ?? 0,
            chapter?.Title,
            state.Position?.SegmentIndex ?? 0,
            chapter?.Segments.Count ?? 0,
            state.Provider?.ProviderId,
            state.Provider?.ProviderName,
            state.SpeakSpeed,
            state.Audio.HasLoadedAudio || state.State == PlaybackState.Stopped
                ? state.Audio.PositionMilliseconds : state.ResumePositionMilliseconds,
            state.Audio.DurationMilliseconds,
            state.Message,
            state.Audio.IsUsingCache,
            state.CanRetry,
            state.Book?.BookAuthor,
            state.Identity is null || state.Provider is not null,
            state.ContentRevision,
            PlaybackVolume.Normalize(volume),
            state.Audio.HasLoadedAudio,
            state.Book?.SourceContext);
    }
}
