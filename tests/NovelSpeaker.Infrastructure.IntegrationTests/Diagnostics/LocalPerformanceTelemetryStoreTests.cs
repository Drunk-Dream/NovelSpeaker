using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Diagnostics;

public sealed class LocalPerformanceTelemetryStoreTests
{
    [DirectoryLinkFact]
    public async Task Writer_rejects_a_telemetry_directory_link_before_writing_outside_the_root()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        var outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        DirectoryLinkTestHelper.CreateDirectoryLink(Path.Combine(fixture.Root, "Telemetry"), outside);
        await using var store = fixture.CreateStore();
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(10));
        await store.DisposeAsync();

        Assert.True(store.IsDegraded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public async Task Disabled_by_default_does_not_create_samples()
    {
        var fixture = new Fixture(AppSettings.Default);
        await using var store = fixture.CreateStore();
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(10));
        await store.DisposeAsync();

        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "novelspeaker-telemetry-*.jsonl", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task History_can_be_exported_after_toggle_off()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        await using var store = fixture.CreateStore();
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(10));
        await store.DisposeAsync();

        Assert.NotEmpty(Directory.EnumerateFiles(fixture.Root, "novelspeaker-telemetry-*.jsonl", SearchOption.AllDirectories));

        var settings = fixture.Settings;
        settings.SetEnabled(false);
        await using var disabledStore = fixture.CreateStore();
        RecordOperation(fixture, disabledStore, TimeSpan.FromMilliseconds(20));
        var exportPath = Path.Combine(fixture.Root, "diagnostics.zip");
        await disabledStore.ExportAsync(exportPath, CancellationToken.None);

        using var archive = ZipFile.OpenRead(exportPath);
        Assert.Equal(
            ["summary.md", "telemetry.json", "logs.jsonl", "environment.json"],
            archive.Entries.Select(entry => entry.FullName).ToArray());
        var telemetry = await ReadJsonAsync(archive, "telemetry.json");
        Assert.Equal(1, telemetry.GetProperty("collection").GetProperty("windowCount").GetInt32());
        Assert.Equal(2, telemetry.GetProperty("aggregates").GetArrayLength());
        Assert.Equal(5, telemetry.GetProperty("metricDefinitions").GetArrayLength());
        Assert.Single(telemetry.GetProperty("windows").EnumerateArray());
        Assert.Equal(1, telemetry.GetProperty("coverage").GetProperty("processInstanceCount").GetInt32());
        Assert.False(settings.Current.EnablePerformanceTelemetry);
    }

    [Fact]
    public async Task Export_includes_retained_windows_older_than_fourteen_days()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        await using var store = fixture.CreateStore();
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(10));
        store.SetCollectionEnabled(false);
        fixture.Clock.Advance(TimeSpan.FromDays(15));

        var exportPath = Path.Combine(fixture.Root, "older-than-fourteen-days.zip");
        await store.ExportAsync(exportPath, CancellationToken.None);

        using var archive = ZipFile.OpenRead(exportPath);
        var telemetry = await ReadJsonAsync(archive, "telemetry.json");
        Assert.Equal(1, telemetry.GetProperty("windows").GetArrayLength());
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            telemetry.GetProperty("coverage").GetProperty("earliestWindowStartUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task Legacy_window_without_process_instance_id_exports_as_null()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        await using var store = fixture.CreateStore();
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(10));
        await store.FlushAsync(CancellationToken.None);

        var telemetryPath = Directory.EnumerateFiles(fixture.Root, "novelspeaker-telemetry-*.jsonl", SearchOption.AllDirectories)
            .Single();
        var legacyLine = (await File.ReadAllTextAsync(telemetryPath))
            .Replace("\"processInstanceId\":\"test-process-instance\",", string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(telemetryPath, legacyLine);

        var exportPath = Path.Combine(fixture.Root, "legacy.zip");
        await store.ExportAsync(exportPath, CancellationToken.None);

        using var archive = ZipFile.OpenRead(exportPath);
        var telemetry = await ReadJsonAsync(archive, "telemetry.json");
        Assert.Equal(0, telemetry.GetProperty("coverage").GetProperty("processInstanceCount").GetInt32());
        Assert.True(telemetry.GetProperty("windows")[0].GetProperty("processInstanceId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Export_to_an_external_directory_overwrites_an_existing_complete_target()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        await using var store = fixture.CreateStore();
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(10));
        var exportDirectory = Path.Combine(Path.GetTempPath(), "NovelSpeaker-External-Export-" + Path.GetRandomFileName());
        Directory.CreateDirectory(exportDirectory);
        var exportPath = Path.Combine(exportDirectory, "diagnostics.zip");
        await File.WriteAllTextAsync(exportPath, "previous export");

        await store.ExportAsync(exportPath, CancellationToken.None);

        using var archive = ZipFile.OpenRead(exportPath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "telemetry.json");
        Assert.Empty(Directory.EnumerateFiles(exportDirectory, ".diagnostics.zip.*.tmp"));
    }

    [Fact]
    public async Task Export_succeeds_while_production_logs_are_being_written_and_rotated()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        await using var store = fixture.CreateStore();
        var outputDirectory = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Concurrent-Export-" + Path.GetRandomFileName());
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "diagnostics.zip");
        await using var provider = new RollingFileLoggerProvider(
            fixture.Directories,
            maxFileBytes: 1024,
            maxTotalBytes: 32 * 1024,
            timeProvider: fixture.Clock);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("ConcurrentDiagnosticExportTests");
        logger.LogWarning("prime the active production log file");
        await provider.FlushAsync();

        var loggingTask = Task.Run(() =>
        {
            for (var index = 0; index < 5_000; index++)
            {
                logger.LogWarning("rotating diagnostic log record {Record}", index);
            }
        });
        await store.ExportAsync(outputPath, CancellationToken.None);
        await loggingTask;
        await provider.FlushAsync();

        using var archive = ZipFile.OpenRead(outputPath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "logs.jsonl");
        Assert.NotEmpty(Directory.EnumerateFiles(fixture.Directories.LogsDirectoryPath, "novelspeaker-*.jsonl"));
    }

    [Fact]
    public async Task Malformed_and_unreadable_logs_are_skipped_without_failing_export()
    {
        var fixture = new Fixture(AppSettings.Default);
        Directory.CreateDirectory(fixture.Directories.LogsDirectoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Directories.LogsDirectoryPath, "malformed.jsonl"),
            "not json\n");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Directories.LogsDirectoryPath, "healthy.jsonl"),
            "{\"timestampUtc\":\"2026-01-01T00:00:00+00:00\",\"level\":\"Warning\",\"eventName\":\"app.test\"}\n");
        var unreadablePath = Path.Combine(fixture.Directories.LogsDirectoryPath, "locked.jsonl");
        await File.WriteAllTextAsync(unreadablePath, "{}\n");
        await using var exclusiveLock = new FileStream(unreadablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await using var store = fixture.CreateStore();
        var exportPath = Path.Combine(fixture.Root, "best-effort.zip");

        await store.ExportAsync(exportPath, CancellationToken.None);

        using var archive = ZipFile.OpenRead(exportPath);
        var logs = await ReadTextAsync(archive, "logs.jsonl");
        Assert.Contains("app.test", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_export_writes_a_redacted_structured_failure_event()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        await using var provider = new RollingFileLoggerProvider(fixture.Directories);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var reporter = new DiagnosticFailureReporter(factory.CreateLogger<DiagnosticFailureReporter>());
        await using var store = new LocalPerformanceTelemetryStore(
            fixture.Directories,
            fixture.Settings,
            fixture.Clock,
            fixture.Context,
            failureReporter: reporter);
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(10));
        var destination = Path.Combine(fixture.Root, "directory-target.zip");
        Directory.CreateDirectory(destination);

        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => store.ExportAsync(destination, CancellationToken.None));
        await provider.FlushAsync();
        factory.Dispose();
        await provider.DisposeAsync();

        var line = Assert.Single(
            Directory.EnumerateFiles(fixture.Directories.LogsDirectoryPath, "novelspeaker-*.jsonl")
                .SelectMany(File.ReadLines));
        using var document = JsonDocument.Parse(line);
        var record = document.RootElement;
        Assert.Equal("diagnostics.operation.failed", record.GetProperty("eventName").GetString());
        Assert.Equal("diagnostics-export", record.GetProperty("properties").GetProperty("DiagnosticOperation").GetString());
        Assert.Equal("commit-bundle", record.GetProperty("properties").GetProperty("Stage").GetString());
        Assert.True(record.GetProperty("exception").GetProperty("hResult").GetInt32() != 0);
        Assert.DoesNotContain(destination, line, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, ".directory-target.zip.*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Clear_keeps_toggle_and_production_logs_untouched()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        Directory.CreateDirectory(fixture.Directories.LogsDirectoryPath);
        var logPath = Path.Combine(fixture.Directories.LogsDirectoryPath, "production.jsonl");
        await File.WriteAllTextAsync(logPath, "{\"level\":\"Warning\"}\n");

        await using var store = fixture.CreateStore();
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(10));
        await store.ClearAsync(CancellationToken.None);

        Assert.True(fixture.Settings.Current.EnablePerformanceTelemetry);
        Assert.True(File.Exists(logPath));
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "novelspeaker-telemetry-*.jsonl", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Merges_histogram_windows_before_calculating_percentiles()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        await using var store = fixture.CreateStore();
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(1));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        RecordOperation(fixture, store, TimeSpan.FromMilliseconds(20_000));

        var exportPath = Path.Combine(fixture.Root, "merged.zip");
        await store.ExportAsync(exportPath, CancellationToken.None);
        using var archive = ZipFile.OpenRead(exportPath);
        var telemetry = await ReadJsonAsync(archive, "telemetry.json");
        var duration = telemetry.GetProperty("aggregates")
            .EnumerateArray()
            .Single(metric => metric.GetProperty("name").GetString() == "operation.duration");

        Assert.Equal(2, duration.GetProperty("count").GetInt64());
        Assert.Equal(1, duration.GetProperty("approximateP50").GetDouble());
        Assert.Equal(20_000, duration.GetProperty("approximateP95").GetDouble());
        Assert.Equal(20_000, duration.GetProperty("approximateP99").GetDouble());
        Assert.Equal(20, duration.GetProperty("buckets").GetArrayLength());
    }

    [Fact]
    public async Task Resource_sampling_is_independent_of_operations_and_restarts_after_toggle()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        await using var store = fixture.CreateStore();
        for (var index = 0; index < 100; index++)
        {
            RecordOperation(fixture, store, TimeSpan.FromMilliseconds(1));
        }

        await store.FlushAsync(CancellationToken.None);
        var operationWindow = Assert.Single(ReadWindows(fixture));
        Assert.Equal(2, operationWindow.GetProperty("metrics").EnumerateObject().Count());

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await store.FlushAsync(CancellationToken.None);
        var windows = ReadWindows(fixture);
        Assert.Contains(windows, window =>
            window.GetProperty("metrics").EnumerateObject().Any(metric =>
                metric.Value.GetProperty("name").GetString() == "process.cpu.percent"));

        store.SetCollectionEnabled(false);
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        await store.FlushAsync(CancellationToken.None);
        Assert.Equal(windows.Length, ReadWindows(fixture).Length);

        store.SetCollectionEnabled(true);
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await store.FlushAsync(CancellationToken.None);
        Assert.Equal(windows.Length, ReadWindows(fixture).Length);
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await store.FlushAsync(CancellationToken.None);
        var resumedWindows = ReadWindows(fixture);
        Assert.Equal(windows.Length + 1, resumedWindows.Length);
        Assert.All(resumedWindows, window => Assert.Equal(
            fixture.Context.Current.ProcessInstanceId,
            window.GetProperty("processInstanceId").GetString()));
        Assert.Equal(
            3,
            resumedWindows[^1].GetProperty("metrics").EnumerateObject().Count());

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Clock.AdjustUtcOffset(TimeSpan.FromHours(-1));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await store.FlushAsync(CancellationToken.None);
        var adjustedWindows = ReadWindows(fixture);
        Assert.Equal(resumedWindows.Length + 2, adjustedWindows.Length);
        Assert.Contains(
            adjustedWindows,
            window => window.GetProperty("metrics").EnumerateObject().Any(metric =>
                metric.Value.GetProperty("name").GetString() == "process.cpu.percent" &&
                window.GetProperty("windowStartUtc").GetDateTimeOffset() < resumedWindows[0].GetProperty("windowStartUtc").GetDateTimeOffset()));
    }

    [Fact]
    public async Task Writer_failure_does_not_change_operation_result()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        Directory.CreateDirectory(fixture.Root);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Telemetry"), "not a directory");
        await using var store = fixture.CreateStore();
        var hub = new ObservabilityHub(fixture.Context, [store], fixture.Clock);
        using var operation = hub.StartOperation(OperationCatalog.PlaybackStart);
        operation.Complete(OperationResult.Succeeded());

        Assert.True(operation.IsCompleted);
        await store.DisposeAsync();
    }

    private static void RecordOperation(Fixture fixture, LocalPerformanceTelemetryStore store, TimeSpan duration)
    {
        var hub = new ObservabilityHub(fixture.Context, [store], fixture.Clock);
        using var operation = hub.StartOperation(OperationCatalog.PlaybackStart);
        fixture.Clock.Advance(duration);
        operation.Complete(OperationResult.Succeeded());
    }

    private static async Task<JsonElement> ReadJsonAsync(ZipArchive archive, string entryName)
    {
        await using var stream = archive.GetEntry(entryName)!.Open();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static JsonElement[] ReadWindows(Fixture fixture) =>
        Directory.EnumerateFiles(fixture.Root, "novelspeaker-telemetry-*.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseWindow)
            .OrderBy(window => window.GetProperty("windowStartUtc").GetDateTimeOffset())
            .ToArray();

    private static JsonElement ParseWindow(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadTextAsync(ZipArchive archive, string entryName)
    {
        await using var stream = archive.GetEntry(entryName)!.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class Fixture
    {
        public Fixture(AppSettings settings)
        {
            Root = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Telemetry-" + Path.GetRandomFileName());
            Directories = new AppDataDirectoryProvider(Root);
            Directories.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
            Settings = new FakeSettings(settings);
            Clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Context = new ObservabilityContextAccessor("test-process-instance");
        }

        public string Root { get; }
        public AppDataDirectoryProvider Directories { get; }
        public FakeSettings Settings { get; }
        public ManualTimeProvider Clock { get; }
        public ObservabilityContextAccessor Context { get; }

        public LocalPerformanceTelemetryStore CreateStore() =>
            new(Directories, Settings, Clock, Context);
    }

    private sealed class FakeSettings(AppSettings settings) : IAppSettingsService
    {
        private AppSettings _current = settings.Normalize();

        public AppSettings Current => _current;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void SetEnabled(bool enabled) => _current = _current with { EnablePerformanceTelemetry = enabled };

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(_current);
    }
}
