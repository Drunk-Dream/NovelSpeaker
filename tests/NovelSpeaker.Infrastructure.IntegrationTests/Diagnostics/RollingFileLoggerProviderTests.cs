using System.Text.Json;
using System.IO;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Infrastructure.Cache;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Diagnostics;

public sealed class RollingFileLoggerProviderTests
{
    [DirectoryLinkFact]
    public async Task Logger_rejects_a_link_at_its_generated_output_path()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var linkedLogFile = Path.Combine(directories.LogsDirectoryPath, "novelspeaker-20260101-000.jsonl");
        DirectoryLinkTestHelper.CreateDirectoryLink(linkedLogFile, outside);

        await using var provider = new RollingFileLoggerProvider(directories, timeProvider: clock);
        provider.CreateLogger("LinkTests").LogInformation("a safe diagnostic event");
        await provider.FlushAsync();

        Assert.True(provider.WriteFailureCount > 0);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public async Task Logger_redacts_sensitive_values_and_rotates_bounded_files()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        await using var provider = new RollingFileLoggerProvider(
            directories,
            maxFileBytes: 220,
            maxTotalBytes: 660);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("Tests");

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            logger.LogInformation("Authorization=secret-{Index} speakText=novel-{TextIndex}", index, index))));

        await provider.FlushAsync();
        factory.Dispose();
        await provider.DisposeAsync();
        var files = Directory.GetFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl");
        var content = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        Assert.InRange(files.Length, 1, 4);
        Assert.DoesNotContain("secret-", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("novel-", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization=***", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagnostic_failure_report_is_structured_without_exception_paths_urls_or_user_text()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await using (var provider = new RollingFileLoggerProvider(directories))
        {
            using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var reporter = new DiagnosticFailureReporter(factory.CreateLogger<DiagnosticFailureReporter>());
            var exception = new IOException(
                "Read failed at C:\\Users\\private\\NovelSpeaker\\secret.log and https://example.invalid/export?token=top-secret novel-title-secret",
                new InvalidOperationException("Inner failure at /home/private/secret novel-body-secret"));

            reporter.ReportFailure(
                NovelSpeaker.Application.Diagnostics.DiagnosticFailureOperation.ProblemDiagnosticsExport,
                NovelSpeaker.Application.Diagnostics.DiagnosticFailureStage.CommitBundle,
                exception);
            await provider.FlushAsync();
        }

        var line = Assert.Single(
            Directory.EnumerateFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl")
                .SelectMany(File.ReadLines));
        using var document = JsonDocument.Parse(line);
        var record = document.RootElement;
        Assert.Equal("diagnostics.operation.failed", record.GetProperty("eventName").GetString());
        Assert.Equal("diagnostics.action", record.GetProperty("operation").GetString());
        Assert.Equal("problem-diagnostics-export", record.GetProperty("properties").GetProperty("DiagnosticOperation").GetString());
        Assert.Equal("commit-bundle", record.GetProperty("properties").GetProperty("Stage").GetString());
        var details = record.GetProperty("exception");
        Assert.Equal(typeof(IOException).FullName, details.GetProperty("type").GetString());
        Assert.Equal(new IOException().HResult, details.GetProperty("hResult").GetInt32());
        Assert.DoesNotContain("C:\\Users\\private", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.log", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("top-secret", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/private", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("novel-title-secret", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("novel-body-secret", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Writer_failure_is_degraded_and_does_not_escape_the_logging_call()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        Directory.Delete(directories.LogsDirectoryPath);
        File.WriteAllText(directories.LogsDirectoryPath, "not a directory");

        await using var provider = new RollingFileLoggerProvider(directories);
        var logger = provider.CreateLogger("FailureTests");

        var exception = Record.Exception(() => logger.LogError(
            LogEventRegistry.StartupFailure.EventId,
            "The writer should degrade."));
        await provider.FlushAsync();

        Assert.Null(exception);
        Assert.True(provider.IsDegraded);
    }
}
