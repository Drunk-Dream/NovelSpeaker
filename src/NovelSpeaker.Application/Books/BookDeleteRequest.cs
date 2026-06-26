namespace NovelSpeaker.Application.Books;

/// <summary>
/// Describes how one imported book should be deleted.
/// </summary>
public sealed record BookDeleteRequest(
    string BookId,
    bool DeleteAudioCache);
