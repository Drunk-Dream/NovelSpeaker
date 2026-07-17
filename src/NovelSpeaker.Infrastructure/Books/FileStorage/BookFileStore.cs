using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using System.Text;

namespace NovelSpeaker.Infrastructure.Books.FileStorage;

/// <summary>
/// Writes normalized book content through a temporary file and atomic move.
/// </summary>
public sealed class BookFileStore : IBookFileStore
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAppStoragePathResolver _pathResolver;

    public BookFileStore(IAppDataDirectoryProvider directories, IAppStoragePathResolver pathResolver)
    {
        _directories = directories;
        _pathResolver = pathResolver;
    }

    public async Task<BookFileCopyHandle> StageNormalizedTextAsync(
        string normalizedText,
        string bookId,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_directories.BooksDirectoryPath, bookId);
        Directory.CreateDirectory(directory);

        var finalPath = Path.Combine(directory, "content.txt");
        var temporaryPath = Path.Combine(directory, "content.txt.tmp");

        await File.WriteAllTextAsync(temporaryPath, normalizedText, Utf8WithoutBom, cancellationToken);
        var encodedLength = Utf8WithoutBom.GetByteCount(normalizedText);
        progress?.Report(new BookImportProgress(
            BookImportPhase.WritingContentFile,
            encodedLength,
            encodedLength,
            true,
            "正在保存规范化正文文件。"));

        return new BookFileCopyHandle(
            _pathResolver.GetStorageKey(finalPath),
            _pathResolver.GetStorageKey(temporaryPath));
    }

    public Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporaryPath = _pathResolver.ResolvePath(copyHandle.TemporaryPath);
        var finalPath = _pathResolver.ResolvePath(copyHandle.FinalPath);
        if (File.Exists(finalPath))
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            return Task.CompletedTask;
        }

        File.Move(temporaryPath, finalPath);
        return Task.CompletedTask;
    }

    public Task CleanupAsync(
        BookFileCopyHandle copyHandle,
        bool includeFinalFile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporaryPath = _pathResolver.ResolvePath(copyHandle.TemporaryPath);
        var finalPath = _pathResolver.ResolvePath(copyHandle.FinalPath);
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        if (includeFinalFile && File.Exists(finalPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(finalPath);
        }

        var directory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(directory) &&
            Directory.Exists(directory) &&
            !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(directory);
        }

        return Task.CompletedTask;
    }
}
