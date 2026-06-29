namespace NovelSpeaker.Infrastructure.Books;

/// <summary>
/// Represents the book metadata derived from a source file name.
/// </summary>
public sealed record BookFileNameMetadataParseResult(
    string SuggestedTitle,
    string? SuggestedAuthor,
    bool IsMatched);
