namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Selects chapters and speech speed for a new process-owned active-cache batch.
/// </summary>
public sealed record StartActiveCacheRequest(
    string BookId,
    IReadOnlyList<int> ChapterIndices,
    int SpeakSpeed);
