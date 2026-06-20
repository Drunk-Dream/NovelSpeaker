namespace NovelSpeaker.Application.Books;

/// <summary>
/// Describes the temporary and final target paths for one copied source TXT file.
/// </summary>
public sealed record BookFileCopyHandle(
    string FinalPath,
    string TemporaryPath);
