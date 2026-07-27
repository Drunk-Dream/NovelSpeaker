namespace NovelSpeaker.Application.Playback.Export;

/// <summary>
/// Requests one operation-scoped export using the configuration current when the operation starts.
/// </summary>
public sealed record ExportChaptersRequest(
    string BookId,
    IReadOnlyCollection<int> ChapterIndices,
    string DestinationRootDirectory);
