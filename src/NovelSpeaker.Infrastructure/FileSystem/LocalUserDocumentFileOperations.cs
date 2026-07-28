using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.FileSystem;

public sealed class LocalUserDocumentFileOperations : IUserDocumentFileOperations
{
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

    public Task WriteTextAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(filePath, content, cancellationToken);
    }
}
