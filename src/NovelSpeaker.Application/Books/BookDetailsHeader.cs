namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents the lightweight metadata needed to paint the book details page shell.
/// </summary>
public sealed record BookDetailsHeader(
    string Id,
    string Title,
    string? Author);
