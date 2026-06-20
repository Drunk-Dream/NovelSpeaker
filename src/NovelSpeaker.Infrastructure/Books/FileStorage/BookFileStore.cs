using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.FileStorage;

/// <summary>
/// Writes imported source files through a temporary file and atomic move.
/// </summary>
public sealed class BookFileStore : IBookFileStore
{
    private readonly IAppDataDirectoryProvider _directories;

    public BookFileStore(IAppDataDirectoryProvider directories)
    {
        _directories = directories;
    }

    public async Task<BookFileCopyHandle> PrepareCopyAsync(string sourceFilePath, string bookId, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_directories.BooksDirectoryPath, bookId);
        Directory.CreateDirectory(directory);

        var finalPath = Path.Combine(directory, "original.txt");
        var temporaryPath = Path.Combine(directory, "original.txt.tmp");

        await using var source = File.OpenRead(sourceFilePath);
        await using var destination = File.Create(temporaryPath);
        await source.CopyToAsync(destination, cancellationToken);

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
