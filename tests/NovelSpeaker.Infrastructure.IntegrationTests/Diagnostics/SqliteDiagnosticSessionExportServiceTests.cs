using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Diagnostics;

public sealed class SqliteDiagnosticSessionExportServiceTests
{
    [Fact]
    public async Task Immediate_and_selected_file_exports_share_the_same_package_shape()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Diagnostic-Export-" + Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var context = new ObservabilityContextAccessor("process-one");
        var clock = new FixedTimeProvider();
        await using var store = new SqliteDiagnosticSessionStore(
            directories,
            context,
            clock,
            new TestAppSettingsService(AppSettings.Default),
            new AppStoragePathResolver(directories));
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        await store.AddAttachmentAsync(
            new DiagnosticAttachment(
                "capture-one",
                clock.GetUtcNow(),
                "image/png",
                2,
                1,
                [1, 2, 3, 4]),
            CancellationToken.None);
        Assert.True(await store.RecordProblemMarkerAsync(CancellationToken.None));
        await store.EndAsync(CancellationToken.None);

        var exporter = new SqliteDiagnosticSessionExportService(store, directories);
        var externalDirectory = root + "-external-exports";
        Directory.CreateDirectory(externalDirectory);
        var immediatePath = Path.Combine(externalDirectory, "immediate.zip");
        var selectedPath = Path.Combine(externalDirectory, "selected.zip");
        await File.WriteAllTextAsync(selectedPath, "previous problem export");
        await exporter.ExportLastEndedAsync(immediatePath, CancellationToken.None);
        await exporter.ExportAsync(
            Path.Combine(directories.DiagnosticsDirectoryPath, $"session-{started.SessionId}.nsdiag"),
            selectedPath,
            CancellationToken.None);

        var immediateEntries = ReadEntries(immediatePath);
        var selectedEntries = ReadEntries(selectedPath);
        Assert.Equal(immediateEntries.Keys.OrderBy(value => value), selectedEntries.Keys.OrderBy(value => value));
        Assert.Contains("summary.md", immediateEntries.Keys);
        Assert.Contains("timeline.md", immediateEntries.Keys);
        Assert.Contains("session.nsdiag", immediateEntries.Keys);
        Assert.Contains("logs.jsonl", immediateEntries.Keys);
        Assert.Contains("environment.json", immediateEntries.Keys);
        Assert.Contains("diagnostics-schema.json", immediateEntries.Keys);
        Assert.Contains("attachments/", immediateEntries.Keys);
        Assert.Contains("attachments/capture-one.png", immediateEntries.Keys);
        Assert.Equal([1, 2, 3, 4], immediateEntries["attachments/capture-one.png"]);
        Assert.Contains("diagnostics.problem_marker", Encoding.UTF8.GetString(immediateEntries["diagnostics-schema.json"]));
        Assert.Contains("主动截图数量：1", Encoding.UTF8.GetString(immediateEntries["summary.md"]));
        Assert.Contains("关联生产日志证据：complete", Encoding.UTF8.GetString(immediateEntries["summary.md"]));
        Assert.Empty(immediateEntries["logs.jsonl"]);
    }

    [Fact]
    public async Task Correlated_error_log_is_included_without_unrelated_logs()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Diagnostic-Export-" + Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var context = new ObservabilityContextAccessor("process-one");
        await using var store = new SqliteDiagnosticSessionStore(
            directories, context, new FixedTimeProvider(),
            new TestAppSettingsService(AppSettings.Default), new AppStoragePathResolver(directories));
        await using (var provider = new RollingFileLoggerProvider(directories, context, timeProvider: new FixedTimeProvider()))
        {
            using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var logger = factory.CreateLogger("ExportCorrelationTests");
            var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
            logger.LogError(LogEventRegistry.TtsRequestFailed.EventId, new InvalidOperationException("private text"),
                "TTS request failed.");
            await store.EndAsync(CancellationToken.None);
            using (context.Push(new CorrelationContext("process-one", "unrelated-session")))
            {
                logger.LogError(LogEventRegistry.TtsRequestFailed.EventId, "Unrelated request failed.");
            }

            await provider.FlushAsync();
            var output = Path.Combine(root, "correlated.zip");
            await new SqliteDiagnosticSessionExportService(store, directories)
                .ExportLastEndedAsync(output, CancellationToken.None);
            var entries = ReadEntries(output);
            var logs = Encoding.UTF8.GetString(entries["logs.jsonl"]);
            var line = Assert.Single(logs.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            Assert.Equal(started.SessionId, document.RootElement.GetProperty("diagnosticSessionId").GetString());
            Assert.Equal("System.InvalidOperationException",
                document.RootElement.GetProperty("exception").GetProperty("type").GetString());
            Assert.Contains("关联生产日志证据：complete", Encoding.UTF8.GetString(entries["summary.md"]));
        }
    }

    [Fact]
    public async Task Malformed_json_and_utf8_records_do_not_hide_later_correlated_log()
    {
        var (store, directories, sessionId) = await CreateEndedSessionAsync();
        await using var ownedStore = store;
        var path = Path.Combine(directories.LogsDirectoryPath, "novelspeaker-20260101-000.jsonl");
        await File.WriteAllBytesAsync(path,
        [
            .. Encoding.UTF8.GetBytes("not json\n"),
            0xFF, (byte)'\n',
            .. Encoding.UTF8.GetBytes("{\"diagnosticSessionId\":\"" + sessionId + "\",\"eventName\":\"fatal\"}\n")
        ]);
        var output = Path.Combine(directories.RootDirectoryPath, "malformed.zip");

        await new SqliteDiagnosticSessionExportService(store, directories)
            .ExportLastEndedAsync(output, CancellationToken.None);

        var entries = ReadEntries(output);
        Assert.Contains("\"eventName\":\"fatal\"", Encoding.UTF8.GetString(entries["logs.jsonl"]));
        Assert.DoesNotContain("not json", Encoding.UTF8.GetString(entries["logs.jsonl"]));
        Assert.Contains("关联生产日志证据：partial", Encoding.UTF8.GetString(entries["summary.md"]));
    }

    [Fact]
    public async Task Unreadable_log_is_visible_without_failing_export_or_failure_reporting()
    {
        var (store, directories, sessionId) = await CreateEndedSessionAsync();
        await using var ownedStore = store;
        var healthyPath = Path.Combine(directories.LogsDirectoryPath, "novelspeaker-20260101-000.jsonl");
        await File.WriteAllTextAsync(healthyPath,
            "{\"diagnosticSessionId\":\"" + sessionId + "\",\"eventName\":\"healthy\"}\n");
        var lockedPath = Path.Combine(directories.LogsDirectoryPath, "novelspeaker-20260101-001.jsonl");
        await File.WriteAllTextAsync(lockedPath, "{}\n");
        await using var exclusiveLock = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await using var provider = new RollingFileLoggerProvider(directories);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var reporter = new DiagnosticFailureReporter(factory.CreateLogger<DiagnosticFailureReporter>());
        var output = Path.Combine(directories.RootDirectoryPath, "partial.zip");

        await new SqliteDiagnosticSessionExportService(store, directories, failureReporter: reporter)
            .ExportLastEndedAsync(output, CancellationToken.None);
        await provider.FlushAsync();
        await provider.DisposeAsync();

        var entries = ReadEntries(output);
        Assert.Contains("\"eventName\":\"healthy\"", Encoding.UTF8.GetString(entries["logs.jsonl"]));
        Assert.Contains("关联生产日志证据：partial", Encoding.UTF8.GetString(entries["summary.md"]));
        Assert.Contains(Directory.EnumerateFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl")
                .Where(path => path != lockedPath).SelectMany(File.ReadLines),
            line => line.Contains("\"Stage\":\"read-logs\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task All_unreadable_logs_are_unavailable_and_bundle_is_still_written()
    {
        var (store, directories, _) = await CreateEndedSessionAsync();
        await using var ownedStore = store;
        var lockedPath = Path.Combine(directories.LogsDirectoryPath, "novelspeaker-20260101-000.jsonl");
        await File.WriteAllTextAsync(lockedPath, "{}\n");
        await using var exclusiveLock = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var output = Path.Combine(directories.RootDirectoryPath, "unavailable.zip");

        await new SqliteDiagnosticSessionExportService(store, directories)
            .ExportLastEndedAsync(output, CancellationToken.None);

        var entries = ReadEntries(output);
        Assert.Empty(entries["logs.jsonl"]);
        Assert.Contains("关联生产日志证据：unavailable", Encoding.UTF8.GetString(entries["summary.md"]));
    }

    [Fact]
    public async Task Fully_corrupted_log_is_unavailable()
    {
        var (store, directories, _) = await CreateEndedSessionAsync();
        await using var ownedStore = store;
        var path = Path.Combine(directories.LogsDirectoryPath, "novelspeaker-20260101-000.jsonl");
        await File.WriteAllTextAsync(path, "not json\n");
        var output = Path.Combine(directories.RootDirectoryPath, "corrupted.zip");

        await new SqliteDiagnosticSessionExportService(store, directories)
            .ExportLastEndedAsync(output, CancellationToken.None);

        var entries = ReadEntries(output);
        Assert.Empty(entries["logs.jsonl"]);
        Assert.Contains("关联生产日志证据：unavailable", Encoding.UTF8.GetString(entries["summary.md"]));
    }

    [Fact]
    public async Task A_failed_export_does_not_modify_the_source_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Diagnostic-Export-" + Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await using var store = new SqliteDiagnosticSessionStore(
            directories,
            new ObservabilityContextAccessor("process-one"),
            new FixedTimeProvider(),
            new TestAppSettingsService(AppSettings.Default),
            new AppStoragePathResolver(directories));
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        await store.EndAsync(CancellationToken.None);
        var source = Path.Combine(directories.DiagnosticsDirectoryPath, $"session-{started.SessionId}.nsdiag");
        var before = await File.ReadAllBytesAsync(source);
        var exporter = new SqliteDiagnosticSessionExportService(store, directories);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(source, source, CancellationToken.None));

        Assert.Equal(before, await File.ReadAllBytesAsync(source));
    }

    private static Dictionary<string, byte[]> ReadEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            });
    }

    private static async Task<(SqliteDiagnosticSessionStore Store, AppDataDirectoryProvider Directories, string SessionId)>
        CreateEndedSessionAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Diagnostic-Export-" + Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new SqliteDiagnosticSessionStore(
            directories, new ObservabilityContextAccessor("process-one"), new FixedTimeProvider(),
            new TestAppSettingsService(AppSettings.Default), new AppStoragePathResolver(directories));
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        await store.EndAsync(CancellationToken.None);
        return (store, directories, started.SessionId);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
