namespace NovelSpeaker.Application.Books.Import;

/// <summary>
/// Represents book metadata derived from a source file name.
/// </summary>
public sealed record BookFileNameMetadataParseResult(
    string SuggestedTitle,
    string? SuggestedAuthor,
    bool IsMatched);
