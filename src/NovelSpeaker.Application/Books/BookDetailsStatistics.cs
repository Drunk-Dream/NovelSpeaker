namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents independent detail statistics that are not part of the chapter catalog.
/// </summary>
public sealed record BookDetailsStatistics(long CachedAudioBytes);
