using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
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
        var immediatePath = Path.Combine(root, "immediate.zip");
        var selectedPath = Path.Combine(root, "selected.zip");
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

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
