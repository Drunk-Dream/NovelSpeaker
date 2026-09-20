using System.IO.Compression;

namespace NovelSpeaker.Infrastructure.Diagnostics;

internal sealed class DiagnosticBundleWriter
{
    public async Task WriteAsync(
        string destinationPath,
        Func<ZipArchive, CancellationToken, Task> buildArchiveAsync,
        CancellationToken cancellationToken,
        Action? beforeCommit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(buildArchiveAsync);
        cancellationToken.ThrowIfCancellationRequested();

        var outputPath = Path.GetFullPath(destinationPath);
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new ArgumentException("Export target must have a parent directory.", nameof(destinationPath));
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await buildArchiveAsync(archive, cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            beforeCommit?.Invoke();
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
