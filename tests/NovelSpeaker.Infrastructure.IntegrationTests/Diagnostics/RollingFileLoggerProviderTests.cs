using Microsoft.Extensions.Logging;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Diagnostics;

public sealed class RollingFileLoggerProviderTests
{
    [Fact]
    public async Task Logger_redacts_sensitive_values_and_rotates_bounded_files()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        using var provider = new RollingFileLoggerProvider(directories, maxFileBytes: 80, maxFileCount: 3);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("Tests");

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            logger.LogInformation("Authorization=secret-{Index} speakText=novel-{TextIndex}", index, index))));

        var files = Directory.GetFiles(directories.LogsDirectoryPath, "novelspeaker*.log");
        var content = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        Assert.InRange(files.Length, 1, 3);
        Assert.DoesNotContain("secret-", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("novel-", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization=***", content, StringComparison.OrdinalIgnoreCase);
    }
}
