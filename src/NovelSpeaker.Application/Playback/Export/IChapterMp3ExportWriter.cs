using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.Application.Playback.Export;

/// <summary>
/// Owns cache-file leases, audio decoding/encoding, staging files and atomic non-overwriting publication.
/// </summary>
public interface IChapterMp3ExportWriter
{
    Task<ChapterMp3ExportWriteResult> WriteAsync(
        ChapterMp3ExportBatch batch,
        CancellationToken cancellationToken);
}

public sealed record ChapterMp3ExportBatch(
    string DestinationRootDirectory,
    string BookDirectoryName,
    IReadOnlyList<ChapterMp3ExportPlan> Chapters);

public sealed record ChapterMp3ExportPlan(
    int ChapterIndex,
    string FileNameBase,
    IReadOnlyList<AudioCacheKey> OrderedSegmentKeys);

public enum ChapterMp3ExportWriteStatus
{
    Succeeded,
    IncompleteCache
}

public sealed record ChapterMp3ExportWriteResult(
    ChapterMp3ExportWriteStatus Status,
    string? ExportDirectoryPath,
    IReadOnlyList<ExportedChapterMp3> Files,
    int? IncompleteChapterIndex)
{
    public static ChapterMp3ExportWriteResult Succeeded(
        string exportDirectoryPath,
        IReadOnlyList<ExportedChapterMp3> files) =>
        new(ChapterMp3ExportWriteStatus.Succeeded, exportDirectoryPath, files, null);

    public static ChapterMp3ExportWriteResult IncompleteCache(int chapterIndex) =>
        new(ChapterMp3ExportWriteStatus.IncompleteCache, null, [], chapterIndex);
}
