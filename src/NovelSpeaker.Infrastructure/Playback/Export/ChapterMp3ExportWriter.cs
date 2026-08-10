using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.Infrastructure.Persistence.Playback;

namespace NovelSpeaker.Infrastructure.Playback.Export;

/// <summary>
/// Publishes chapter MP3 files using protected cache inputs, same-directory staging and atomic moves.
/// </summary>
internal sealed class ChapterMp3ExportWriter : IChapterMp3ExportWriter
{
    private const int MaximumPortablePathLength = 240;
    private const int MaximumCollisionNumber = 999_999;
    private readonly AudioCacheFacade _cache;
    private readonly ExportFileNameSanitizer _fileNameSanitizer;
    private readonly IChapterMp3Encoder _encoder;

    public ChapterMp3ExportWriter(
        AudioCacheFacade cache,
        ExportFileNameSanitizer fileNameSanitizer,
        IChapterMp3Encoder encoder)
    {
        _cache = cache;
        _fileNameSanitizer = fileNameSanitizer;
        _encoder = encoder;
    }

    public async Task<ChapterMp3ExportWriteResult> WriteAsync(
        ChapterMp3ExportBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(batch.DestinationRootDirectory);
        ArgumentNullException.ThrowIfNull(batch.Chapters);
        if (batch.Chapters.Count == 0)
        {
            throw new ArgumentException("At least one chapter export plan is required.", nameof(batch));
        }

        if (batch.Chapters.Any(chapter => chapter.OrderedSegmentKeys.Count == 0))
        {
            throw new ArgumentException("Every chapter must contain at least one segment key.", nameof(batch));
        }

        var orderedKeys = batch.Chapters
            .SelectMany(chapter => chapter.OrderedSegmentKeys)
            .ToArray();
        var acquisition = await _cache
            .AcquireExportLeaseAsync(orderedKeys, cancellationToken)
            .ConfigureAwait(false);
        if (acquisition.Lease is null)
        {
            var incompleteChapter = batch.Chapters.First(
                chapter => chapter.OrderedSegmentKeys.Contains(acquisition.MissingKey!));
            return ChapterMp3ExportWriteResult.IncompleteCache(incompleteChapter.ChapterIndex);
        }

        using var lease = acquisition.Lease;
        var rootDirectory = ResolveRootDirectory(batch.DestinationRootDirectory);
        var bookDirectoryName = _fileNameSanitizer.Sanitize(batch.BookDirectoryName, 80);
        var bookDirectory = ResolveChildPath(rootDirectory, bookDirectoryName);
        EnsurePathLength(bookDirectory);
        RejectReparsePoint(bookDirectory);
        Directory.CreateDirectory(bookDirectory);
        RejectReparsePoint(bookDirectory);

        var exported = new List<ExportedChapterMp3>(batch.Chapters.Count);
        var sourceOffset = 0;
        foreach (var chapter in batch.Chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batch.Progress?.Report(new ExportChaptersProgress(
                exported.Count,
                batch.Chapters.Count,
                chapter.ChapterIndex));
            var sourcePaths = lease.OrderedFilePaths
                .Skip(sourceOffset)
                .Take(chapter.OrderedSegmentKeys.Count)
                .ToArray();
            sourceOffset += sourcePaths.Length;
            var stagingPath = ResolveChildPath(
                bookDirectory,
                $".novelspeaker-export-{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var destination = new FileStream(
                                 stagingPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 64 * 1024,
                                 options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await _encoder
                        .EncodeAsync(sourcePaths, destination, cancellationToken)
                        .ConfigureAwait(false);
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                    destination.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = MoveWithoutOverwrite(
                    stagingPath,
                    bookDirectory,
                    chapter.FileNameBase,
                    cancellationToken);
                exported.Add(new ExportedChapterMp3(chapter.ChapterIndex, outputPath));
                batch.Progress?.Report(new ExportChaptersProgress(
                    exported.Count,
                    batch.Chapters.Count,
                    chapter.ChapterIndex));
            }
            finally
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }
            }
        }

        return ChapterMp3ExportWriteResult.Succeeded(bookDirectory, exported);
    }

    private string MoveWithoutOverwrite(
        string stagingPath,
        string bookDirectory,
        string requestedFileNameBase,
        CancellationToken cancellationToken)
    {
        for (var number = 1; number <= MaximumCollisionNumber; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suffix = number == 1 ? string.Empty : $" ({number})";
            var maximumBaseLength = MaximumPortablePathLength -
                                    bookDirectory.Length -
                                    1 -
                                    suffix.Length -
                                    ".mp3".Length;
            if (maximumBaseLength <= 0)
            {
                throw new PathTooLongException("The selected export directory is too long.");
            }

            var safeBase = _fileNameSanitizer.Sanitize(
                requestedFileNameBase,
                maximumBaseLength);
            var outputPath = ResolveChildPath(
                bookDirectory,
                $"{safeBase}{suffix}.mp3");
            try
            {
                File.Move(stagingPath, outputPath);
                return outputPath;
            }
            catch (IOException) when (File.Exists(outputPath))
            {
                continue;
            }
        }

        throw new IOException("No available export file name could be allocated.");
    }

    private static string ResolveRootDirectory(string rootDirectory)
    {
        if (!Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException("The export root directory must be fully qualified.", nameof(rootDirectory));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
    }

    private static string ResolveChildPath(string parentDirectory, string childName)
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentDirectory));
        var candidate = Path.GetFullPath(Path.Combine(parent, childName));
        var prefix = parent + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(prefix, comparison))
        {
            throw new InvalidDataException("The export path escapes the selected directory.");
        }

        return candidate;
    }

    private static void EnsurePathLength(string path)
    {
        if (path.Length >= MaximumPortablePathLength - ".mp3".Length - 1)
        {
            throw new PathTooLongException("The selected export directory is too long.");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The export directory cannot be a reparse point.");
        }
    }
}
