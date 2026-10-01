using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class UserDocumentFileOperationsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_overwrite_preserves_existing_private_backup_and_cleans_temporary_file(bool cancel)
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        var target = Path.Combine(directory.Path, "backup.json");
        await File.WriteAllTextAsync(target, "previous complete backup");
        using var cancellation = new CancellationTokenSource();
        var files = new LocalUserDocumentFileOperations(path => new InterruptedWriteStream(path, cancel, cancellation));

        await Assert.ThrowsAnyAsync<Exception>(() => files.WriteTextAsync(target, new string('a', 10000), cancellation.Token));

        Assert.Equal("previous complete backup", await File.ReadAllTextAsync(target));
        Assert.Equal([target], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task Completed_overwrite_replaces_entire_file_without_leaving_temporary_files()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        var target = Path.Combine(directory.Path, "backup.json");
        await File.WriteAllTextAsync(target, "previous backup");
        await new LocalUserDocumentFileOperations().WriteTextAsync(target, "new complete backup 凭据", CancellationToken.None);
        Assert.Equal("new complete backup 凭据", await File.ReadAllTextAsync(target));
        Assert.Equal([target], Directory.GetFiles(directory.Path));
    }

    private sealed class InterruptedWriteStream(string path, bool cancel, CancellationTokenSource cancellation)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer[..Math.Min(buffer.Length, 16)], cancellationToken);
            if (cancel)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            throw new IOException("Test failure after a partial write.");
        }
    }
}
