namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Identifies the narrowest known cache scope affected by a committed mutation.
/// Null values mean that callers should refresh the containing scope.
/// </summary>
public sealed record CacheChangedEventArgs(
    string? BookId,
    int? ChapterIndex);
