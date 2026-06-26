namespace NovelSpeaker.Application.Books;

/// <summary>
/// Describes one metadata update request for an existing imported book.
/// </summary>
public sealed record BookMetadataUpdateRequest(
    string BookId,
    string Title,
    string? Author);
