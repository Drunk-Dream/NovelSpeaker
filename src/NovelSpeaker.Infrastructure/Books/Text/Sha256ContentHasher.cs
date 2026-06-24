using System.Security.Cryptography;
using System.Text;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.Text;

/// <summary>
/// Computes lowercase SHA-256 hashes for imported source files.
/// </summary>
public sealed class Sha256ContentHasher : IContentHasher
{
    public async Task<string> ComputeFileHashAsync(
        string filePath,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var totalBytes = stream.Length;
        var buffer = new byte[81920];
        long bytesProcessed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            sha256.TransformBlock(buffer, 0, read, null, 0);
            bytesProcessed += read;
            progress?.Report(new BookImportProgress(
                BookImportPhase.HashingContent,
                bytesProcessed,
                totalBytes,
                totalBytes == 0,
                "正在计算文件指纹。"));
        }

        sha256.TransformFinalBlock([], 0, 0);
        var hash = sha256.Hash ?? [];

        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2"));
        }

        return builder.ToString();
    }
}
