using NovelSpeaker.Application.Abstractions;
using System.Text;

namespace NovelSpeaker.Infrastructure.FileSystem;

public sealed class LocalUserDocumentFileOperations : IUserDocumentFileOperations
{
    private readonly Func<string, Stream> _createTemporaryFile;

    public LocalUserDocumentFileOperations() : this(path => new FileStream(path, FileMode.CreateNew,
        FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
    { }

    internal LocalUserDocumentFileOperations(Func<string, Stream> createTemporaryFile) =>
        _createTemporaryFile = createTemporaryFile;

    public Task<UserDocumentFileMetadata?> GetMetadataAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => GetMetadata(filePath), cancellationToken);
    }

    private static UserDocumentFileMetadata? GetMetadata(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) ||
            Directory.Exists(filePath) ||
            !File.Exists(filePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(filePath);
        return new UserDocumentFileMetadata(
            filePath,
            fileInfo.Name,
            fileInfo.Extension,
            fileInfo.Length);
    }

    public Task<string> ReadTextAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        return File.ReadAllTextAsync(filePath, cancellationToken);
    }

    public async Task WriteTextAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Path.GetFullPath(filePath);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = await Task.Run(() => _createTemporaryFile(temporary), cancellationToken).ConfigureAwait(false))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (stream is FileStream fileStream)
                    await Task.Run(() => fileStream.Flush(flushToDisk: true), cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => File.Move(temporary, target, overwrite: true), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await Task.Run(() => File.Delete(temporary)).ConfigureAwait(false);
        }
    }
}
