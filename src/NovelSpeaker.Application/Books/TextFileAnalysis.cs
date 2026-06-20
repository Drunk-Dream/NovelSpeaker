namespace NovelSpeaker.Application.Books;

/// <summary>
/// Carries the decoded text and preview snippet from a TXT file.
/// </summary>
public sealed record TextFileAnalysis(
    string EncodingName,
    string PreviewText,
    string RawText);
