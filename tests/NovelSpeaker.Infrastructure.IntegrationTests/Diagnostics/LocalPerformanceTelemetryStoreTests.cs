using System.IO.Compression;
using System.Text.Json;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
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
        Assert.Equal(1, telemetry.GetProperty("windowCount").GetInt32());
        Assert.Equal(5, telemetry.GetProperty("metrics").GetArrayLength());
        Assert.False(settings.Current.EnablePerformanceTelemetry);
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
        var duration = telemetry.GetProperty("metrics")
            .EnumerateArray()
            .Single(metric => metric.GetProperty("name").GetString() == "operation.duration");

        Assert.Equal(2, duration.GetProperty("count").GetInt64());
        Assert.Equal(1, duration.GetProperty("approximateP50").GetDouble());
        Assert.Equal(10_000, duration.GetProperty("approximateP95").GetDouble());
        Assert.Equal(10_000, duration.GetProperty("approximateP99").GetDouble());
        Assert.Equal(13, duration.GetProperty("buckets").GetArrayLength());
    }

    [Fact]
    public async Task Writer_failure_does_not_change_operation_result()
    {
        var fixture = new Fixture(AppSettings.Default with { EnablePerformanceTelemetry = true });
        Directory.CreateDirectory(fixture.Root);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Telemetry"), "not a directory");
        await using var store = fixture.CreateStore();
        var hub = new ObservabilityHub(new ObservabilityContextAccessor(), [store], fixture.Clock);
        using var operation = hub.StartOperation(OperationCatalog.PlaybackStart);
        operation.Complete(OperationResult.Succeeded());

        Assert.True(operation.IsCompleted);
        await store.DisposeAsync();
    }

    private static void RecordOperation(Fixture fixture, LocalPerformanceTelemetryStore store, TimeSpan duration)
    {
        var hub = new ObservabilityHub(new ObservabilityContextAccessor(), [store], fixture.Clock);
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

    private sealed class Fixture
    {
        public Fixture(AppSettings settings)
        {
            Root = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Telemetry-" + Path.GetRandomFileName());
            Directories = new AppDataDirectoryProvider(Root);
            Directories.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
            Settings = new FakeSettings(settings);
            Clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }

        public string Root { get; }
        public AppDataDirectoryProvider Directories { get; }
        public FakeSettings Settings { get; }
        public ManualTimeProvider Clock { get; }

        public LocalPerformanceTelemetryStore CreateStore() =>
            new(Directories, Settings, Clock);
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

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
