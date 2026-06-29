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

    public BookFileStore(IAppDataDirectoryProvider directories)
    {
        _directories = directories;
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

        return new BookFileCopyHandle(finalPath, temporaryPath);
    }

    public Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(copyHandle.TemporaryPath, copyHandle.FinalPath, overwrite: true);
        return Task.CompletedTask;
    }

    public Task CleanupAsync(BookFileCopyHandle copyHandle)
    {
        if (File.Exists(copyHandle.TemporaryPath))
        {
            File.Delete(copyHandle.TemporaryPath);
        }

        return Task.CompletedTask;
    }
}
