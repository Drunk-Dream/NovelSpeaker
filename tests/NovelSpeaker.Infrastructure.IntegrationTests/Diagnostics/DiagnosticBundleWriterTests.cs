using System.IO.Compression;
using NovelSpeaker.Infrastructure.Diagnostics;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Diagnostics;

public sealed class DiagnosticBundleWriterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_bundle_build_preserves_target_and_cleans_temporary_file(bool targetExists)
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Bundle-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "diagnostics.zip");
        var original = "existing-target-must-survive"u8.ToArray();
        if (targetExists)
        {
            await File.WriteAllBytesAsync(target, original);
        }

        var writer = new DiagnosticBundleWriter();

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(
            target,
            (archive, _) =>
            {
                var entry = archive.CreateEntry("partial.txt");
                using (var stream = entry.Open())
                using (var text = new StreamWriter(stream))
                {
                    text.Write("partial bundle");
                }

                throw new IOException("injected bundle failure");
            },
            CancellationToken.None));

        Assert.Equal(targetExists, File.Exists(target));
        if (targetExists)
        {
            Assert.Equal(original, await File.ReadAllBytesAsync(target));
        }

        Assert.Empty(Directory.EnumerateFiles(directory, ".diagnostics.zip.*.tmp"));
    }

    [Fact]
    public async Task Successful_bundle_replaces_an_existing_target_with_a_complete_zip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Bundle-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "diagnostics.zip");
        await File.WriteAllTextAsync(target, "old content");
        var writer = new DiagnosticBundleWriter();

        await writer.WriteAsync(
            target,
            (archive, _) =>
            {
                var entry = archive.CreateEntry("complete.txt");
                using var stream = entry.Open();
                using var text = new StreamWriter(stream);
                text.Write("complete bundle");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        using var result = ZipFile.OpenRead(target);
        Assert.Equal("complete.txt", Assert.Single(result.Entries).FullName);
        Assert.Empty(Directory.EnumerateFiles(directory, ".diagnostics.zip.*.tmp"));
    }
}
