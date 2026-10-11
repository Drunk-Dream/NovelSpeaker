namespace NovelSpeaker.Application.Books;

/// <summary>
/// Identifies one imported book to delete.
/// </summary>
public sealed record BookDeleteRequest(string BookId);
