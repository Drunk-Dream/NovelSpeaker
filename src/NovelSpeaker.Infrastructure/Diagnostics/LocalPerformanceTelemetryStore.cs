using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Aggregates privacy-reviewed performance metrics into sparse local windows and exports a bounded bundle.
/// </summary>
public sealed class LocalPerformanceTelemetryStore : IPerformanceTelemetryService, IObservabilityConsumer, IAsyncDisposable
{
    private const int ExportSchemaVersion = 2;
    private const int WindowSchemaVersion = 2;
    private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ResourceSampleInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private const long MaxFileBytes = 8L * 1024 * 1024;
    private const long MaxDirectoryBytes = 64L * 1024 * 1024;
    private const int QueueCapacity = 64;

    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly IAppSettingsService _settings;
    private readonly TimeProvider _timeProvider;
    private readonly string _processInstanceId;
    private readonly ITimer _resourceTimer;
    private readonly PerformanceMetricRegistry _registry;
    private readonly IDiagnosticFailureReporter? _failureReporter;
    private readonly DiagnosticBundleWriter _bundleWriter = new();
    private readonly object _gate = new();
    private readonly Channel<WriterItem> _writerQueue = Channel.CreateBounded<WriterItem>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly Task _writerTask;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    private TelemetryWindowRecord? _currentWindow;
    private bool _writerCompleted;
    private bool _degraded;
    private long _droppedWindowCount;
    private ProcessCpuSample? _lastCpuSample;
    private long _resourceSamplingStartTimestamp;
    private long _sampledResourcePeriods;
    private int _collectionEnabled;
    private int _disposed;

    public LocalPerformanceTelemetryStore(
        IAppDataDirectoryProvider directories,
        IAppSettingsService settings,
        TimeProvider timeProvider,
        IObservabilityContextAccessor contextAccessor,
        PerformanceMetricRegistry? registry = null,
        IAppStoragePathResolver? pathResolver = null,
        IDiagnosticFailureReporter? failureReporter = null)
    {
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _pathResolver = pathResolver ?? new AppStoragePathResolver(_directories);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _processInstanceId = (contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor)))
            .Current.ProcessInstanceId;
        _registry = registry ?? PerformanceMetricRegistry.Default;
        _failureReporter = failureReporter;
        _resourceTimer = _timeProvider.CreateTimer(
            static state => ((LocalPerformanceTelemetryStore)state!).SampleProcessResources(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _writerTask = Task.Run(WriterLoopAsync);
        SetCollectionEnabled(_settings.Current.EnablePerformanceTelemetry);
    }

    public bool IsEnabled => Volatile.Read(ref _collectionEnabled) != 0;

    public void SetCollectionEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0 || IsEnabled == enabled)
            {
                return;
            }

            if (enabled)
            {
                try
                {
                    _lastCpuSample = CaptureCpuSample();
                }
                catch
                {
                    _lastCpuSample = null;
                    Volatile.Write(ref _degraded, true);
                }

                _resourceSamplingStartTimestamp = _timeProvider.GetTimestamp();
                _sampledResourcePeriods = 0;
                Volatile.Write(ref _collectionEnabled, 1);
                TryChangeResourceTimer(ResourceSampleInterval, ResourceSampleInterval);
            }
            else
            {
                Volatile.Write(ref _collectionEnabled, 0);
                TryChangeResourceTimer(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _lastCpuSample = null;
            }
        }
    }

    internal bool IsDegraded => Volatile.Read(ref _degraded);

    internal long DroppedWindowCount => Interlocked.Read(ref _droppedWindowCount);

    private void TryChangeResourceTimer(TimeSpan dueTime, TimeSpan period)
    {
        try
        {
            if (!_resourceTimer.Change(dueTime, period))
            {
                Volatile.Write(ref _degraded, true);
            }
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
        }
    }

    public void OnOperationStarted(OperationStarted operation)
    {
    }

    public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent)
    {
        // The first batch is driven by stable operation completion and process snapshots.
        // Event fields are intentionally not copied into the performance store.
    }

    public void OnOperationCompleted(OperationCompleted operation)
    {
        if (!IsEnabled || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (!IsEnabled || Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                var timestamp = operation.CompletedAtUtc;
                AdvanceWindowLocked(timestamp);
                var outcome = operation.Result.Outcome switch
                {
                    OperationOutcome.Succeeded => "succeeded",
                    OperationOutcome.Failed => "failed",
                    _ => "cancelled"
                };
                var tags = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["operation"] = operation.Operation.Id.Value,
                    ["outcome"] = outcome
                };

                AddSampleLocked(_registry.Get("operation.count"), 1d, tags);
                AddSampleLocked(
                    _registry.Get("operation.duration"),
                    Math.Max(0d, operation.Duration.TotalMilliseconds),
                    tags);
            }
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await FlushAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            try
            {
                var directory = GetTelemetryDirectory();
                if (Directory.Exists(directory))
                {
                    foreach (var path in Directory.EnumerateFiles(directory, "novelspeaker-telemetry-*.jsonl"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        File.Delete(_pathResolver.ResolvePath(path));
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                Volatile.Write(ref _degraded, true);
                throw;
            }
        }
    }

    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        var stage = DiagnosticFailureStage.PrepareBundle;
        try
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);

            var now = _timeProvider.GetUtcNow();
            TryApplyRetention(now);
            stage = DiagnosticFailureStage.ReadTelemetry;
            var windows = ReadWindows(cancellationToken);
            var metrics = MergeMetrics(windows);
            stage = DiagnosticFailureStage.ReadLogs;
            var coverageStart = windows.Count == 0 ? now - Retention : windows.Min(window => window.WindowStartUtc);
            var logLines = ReadRelevantLogs(coverageStart, cancellationToken);
            var appVersion = ResolveVersion();
            var coverageEnd = windows.Count == 0
                ? (DateTimeOffset?)null
                : windows.Max(window => window.WindowEndUtc);
            var processInstanceCount = windows
                .Select(window => window.ProcessInstanceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Count();
            var exportedWindows = windows.Select(window => new
            {
                windowStartUtc = window.WindowStartUtc,
                windowEndUtc = window.WindowEndUtc,
                appVersion = window.AppVersion,
                processInstanceId = window.ProcessInstanceId,
                metrics = window.Metrics.Values.ToArray()
            }).ToArray();
            var metricDefinitions = _registry.Definitions.Select(definition => new
            {
                name = definition.Name,
                type = definition.Type.ToString(),
                unit = definition.Unit,
                description = definition.Description,
                histogramBuckets = definition.HistogramBuckets.ToArray(),
                allowedDimensions = definition.AllowedTags
            }).ToArray();

            var telemetryJson = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = ExportSchemaVersion,
                    generatedAtUtc = now,
                    coverage = new
                    {
                        earliestWindowStartUtc = windows.Count == 0 ? (DateTimeOffset?)null : coverageStart,
                        latestWindowEndUtc = coverageEnd,
                        appVersions = windows.Select(window => window.AppVersion)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(version => version, StringComparer.Ordinal)
                            .ToArray(),
                        processInstanceCount
                    },
                    collection = new
                    {
                        windowSize = WindowSize,
                        resourceSampleInterval = ResourceSampleInterval,
                        windowCount = windows.Count,
                        droppedWindowCount = DroppedWindowCount,
                        degraded = IsDegraded
                    },
                    metricDefinitions,
                    aggregates = metrics,
                    windows = exportedWindows
                },
                _jsonOptions);
            var environmentJson = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = ExportSchemaVersion,
                    appVersion,
                    osDescription = Environment.OSVersion.VersionString,
                    frameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                    processorCount = Environment.ProcessorCount,
                    gcServer = System.Runtime.GCSettings.IsServerGC
                },
                _jsonOptions);
            var summary = BuildSummary(
                now,
                coverageStart,
                coverageEnd,
                windows,
                metrics.Count,
                logLines.Count,
                appVersion,
                processInstanceCount,
                DroppedWindowCount,
                IsDegraded);

            stage = DiagnosticFailureStage.BuildBundle;
            await _bundleWriter.WriteAsync(
                destinationPath,
                (archive, _) =>
                {
                    WriteEntry(archive, "summary.md", summary);
                    WriteEntry(archive, "telemetry.json", telemetryJson);
                    WriteEntry(archive, "logs.jsonl", string.Join("\n", logLines) + (logLines.Count == 0 ? string.Empty : "\n"));
                    WriteEntry(archive, "environment.json", environmentJson);
                    return Task.CompletedTask;
                },
                cancellationToken,
                () => stage = DiagnosticFailureStage.CommitBundle).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _failureReporter?.ReportFailure(
                DiagnosticFailureOperation.DiagnosticsExport,
                stage,
                exception);
            throw;
        }
    }

    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TelemetryWindowRecord? pending;
        lock (_gate)
        {
            pending = _currentWindow;
            _currentWindow = null;
        }

        if (pending is not null)
        {
            EnqueueWindow(pending);
        }

        if (Volatile.Read(ref _writerCompleted))
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _writerQueue.Writer.WriteAsync(WriterItem.Flush(completion), cancellationToken).ConfigureAwait(false);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _collectionEnabled, 0);

        var timerDrained = true;
        try
        {
            await _resourceTimer.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            timerDrained = false;
            Volatile.Write(ref _degraded, true);
        }

        try
        {
            if (timerDrained)
            {
                await FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
        }

        _writerQueue.Writer.TryComplete();
        try
        {
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
        }
    }

    private void AdvanceWindowLocked(DateTimeOffset timestamp)
    {
        var start = FloorWindow(timestamp.UtcDateTime);
        if (_currentWindow is null)
        {
            _currentWindow = new TelemetryWindowRecord(start, start + WindowSize, ResolveVersion(), _processInstanceId);
            return;
        }

        if (start == _currentWindow.WindowStartUtc)
        {
            return;
        }

        EnqueueWindow(_currentWindow);
        _currentWindow = new TelemetryWindowRecord(start, start + WindowSize, ResolveVersion(), _processInstanceId);
    }

    private void AddSampleLocked(
        PerformanceMetricDefinition definition,
        double value,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        definition.ValidateTags(tags);
        var key = MetricKey.Create(definition, tags);
        if (!_currentWindow!.Metrics.TryGetValue(key, out var aggregate))
        {
            aggregate = new MetricAggregateRecord(
                definition.Name,
                definition.Type.ToString(),
                definition.Unit,
                new SortedDictionary<string, string>(
                    (tags ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal),
                definition.HistogramBuckets.ToArray(),
                new long[definition.Type == PerformanceMetricType.Histogram ? definition.HistogramBuckets.Count + 1 : 0]);
            _currentWindow.Metrics.Add(key, aggregate);
        }

        aggregate.Count++;
        aggregate.Sum += value;
        aggregate.Min = Math.Min(aggregate.Min, value);
        aggregate.Max = Math.Max(aggregate.Max, value);
        aggregate.Last = value;
        if (definition.Type == PerformanceMetricType.Histogram)
        {
            var index = definition.HistogramBuckets.Count;
            for (var i = 0; i < definition.HistogramBuckets.Count; i++)
            {
                if (value <= definition.HistogramBuckets[i])
                {
                    index = i;
                    break;
                }
            }

            aggregate.Buckets[index]++;
        }
    }

    private void SampleProcessResources()
    {
        try
        {
            lock (_gate)
            {
                if (!IsEnabled || Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                var samplingElapsed = _timeProvider.GetElapsedTime(_resourceSamplingStartTimestamp);
                var elapsedPeriods = samplingElapsed.Ticks / ResourceSampleInterval.Ticks;
                if (elapsedPeriods <= _sampledResourcePeriods)
                {
                    return;
                }

                var timestamp = _timeProvider.GetUtcNow();
                var sample = CaptureCpuSample();
                var managedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
                AdvanceWindowLocked(timestamp);
                _sampledResourcePeriods = elapsedPeriods;
                if (_lastCpuSample is { } baseline)
                {
                    var elapsed = _timeProvider.GetElapsedTime(baseline.Timestamp, sample.Timestamp).TotalSeconds;
                    if (elapsed > 0d)
                    {
                        var cpuPercent = (sample.TotalProcessorTime - baseline.TotalProcessorTime).TotalSeconds /
                            elapsed / Math.Max(1, Environment.ProcessorCount) * 100d;
                        AddSampleLocked(_registry.Get("process.cpu.percent"), Math.Clamp(cpuPercent, 0d, 100d));
                    }
                }

                _lastCpuSample = sample;
                AddSampleLocked(_registry.Get("process.working-set.bytes"), sample.WorkingSetBytes);
                AddSampleLocked(_registry.Get("process.managed-heap.bytes"), managedHeapBytes);
            }
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
        }
    }

    private void EnqueueWindow(TelemetryWindowRecord window)
    {
        if (!_writerQueue.Writer.TryWrite(WriterItem.Window(window)))
        {
            Interlocked.Increment(ref _droppedWindowCount);
            Volatile.Write(ref _degraded, true);
        }
    }

    private async Task WriterLoopAsync()
    {
        TryApplyRetention(_timeProvider.GetUtcNow());
        try
        {
            await foreach (var item in _writerQueue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (item.WindowRecord is not null)
                {
                    try
                    {
                        WriteWindow(item.WindowRecord);
                    }
                    catch
                    {
                        Volatile.Write(ref _degraded, true);
                    }
                }

                item.FlushCompletion?.TrySetResult();
            }
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
        }
        finally
        {
            Volatile.Write(ref _writerCompleted, true);
        }
    }

    private void WriteWindow(TelemetryWindowRecord window)
    {
        var directory = GetTelemetryDirectory();
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(window, _jsonOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(json);
        var now = window.WindowEndUtc.UtcDateTime;
        var date = now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var index = 0;
        string path;
        while (true)
        {
            path = _pathResolver.ResolvePath(
                Path.Combine(directory, $"novelspeaker-telemetry-{date}-{index:000}.jsonl"));
            if (!File.Exists(path) || new FileInfo(path).Length + bytes.Length <= MaxFileBytes)
            {
                break;
            }

            index++;
        }

        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }

        ApplyRetention(directory, _timeProvider.GetUtcNow());
    }

    private IReadOnlyList<TelemetryWindowRecord> ReadWindows(CancellationToken cancellationToken)
    {
        var records = new List<TelemetryWindowRecord>();
        var directory = GetTelemetryDirectory();
        if (!Directory.Exists(directory))
        {
            return records;
        }

        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(directory, "novelspeaker-telemetry-*.jsonl")
                .Select(_pathResolver.ResolvePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
            return records;
        }

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        var record = JsonSerializer.Deserialize<TelemetryWindowRecord>(line, _jsonOptions);
                        if (record is not null)
                        {
                            records.Add(record);
                        }
                    }
                    catch (JsonException)
                    {
                        Volatile.Write(ref _degraded, true);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                Volatile.Write(ref _degraded, true);
            }
        }

        return records.OrderBy(record => record.WindowStartUtc).ToArray();
    }

    private List<ExportMetric> MergeMetrics(IReadOnlyList<TelemetryWindowRecord> windows)
    {
        var merged = new Dictionary<string, ExportMetric>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            foreach (var metric in window.Metrics.Values)
            {
                var date = window.WindowStartUtc.UtcDateTime.ToString(
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
                var key = MetricKey.Create(
                    $"{window.AppVersion}|{date}|{metric.Name}",
                    metric.Type,
                    metric.Unit,
                    metric.Tags) +
                    (string.Equals(metric.Type, PerformanceMetricType.Histogram.ToString(), StringComparison.Ordinal)
                        ? $"|buckets={string.Join(",", metric.HistogramBuckets.Select(bucket => bucket.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}"
                        : string.Empty);
                if (!merged.TryGetValue(key, out var result))
                {
                    result = new ExportMetric(
                        window.AppVersion,
                        date,
                        metric.Name,
                        metric.Type,
                        metric.Unit,
                        new SortedDictionary<string, string>(metric.Tags, StringComparer.Ordinal),
                        metric.HistogramBuckets.ToArray());
                    merged.Add(key, result);
                }

                result.Add(metric);
            }
        }

        return merged.Values.OrderBy(metric => metric.Name, StringComparer.Ordinal)
            .ThenBy(metric => string.Join(";", metric.Tags.Select(pair => $"{pair.Key}={pair.Value}")), StringComparer.Ordinal)
            .ToList();
    }

    private List<string> ReadRelevantLogs(DateTimeOffset rangeStart, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        if (!Directory.Exists(_directories.LogsDirectoryPath))
        {
            return result;
        }

        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(_directories.LogsDirectoryPath, "*.jsonl")
                .Select(_pathResolver.ResolvePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
            return result;
        }

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        var root = document.RootElement;
                        if (!root.TryGetProperty("timestampUtc", out var timestampElement) ||
                            !timestampElement.TryGetDateTimeOffset(out var timestamp) || timestamp < rangeStart)
                        {
                            continue;
                        }

                        var level = root.TryGetProperty("level", out var levelElement) ? levelElement.GetString() : null;
                        var eventName = root.TryGetProperty("eventName", out var eventElement) ? eventElement.GetString() : null;
                        if (level is "Warning" or "Error" or "Critical" || eventName?.StartsWith("app.", StringComparison.Ordinal) == true)
                        {
                            result.Add(line);
                        }
                    }
                    catch (JsonException)
                    {
                        Volatile.Write(ref _degraded, true);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                Volatile.Write(ref _degraded, true);
            }
        }

        return result;
    }

    private static string BuildSummary(
        DateTimeOffset now,
        DateTimeOffset coverageStart,
        DateTimeOffset? coverageEnd,
        IReadOnlyList<TelemetryWindowRecord> windows,
        int metricCount,
        int logCount,
        string appVersion,
        int processInstanceCount,
        long droppedWindowCount,
        bool degraded) =>
        $"# NovelSpeaker diagnostics\n\n" +
        $"Generated (UTC): {now:O}\n" +
        $"Coverage (UTC): {(windows.Count == 0 ? "none" : $"{coverageStart:O} to {coverageEnd:O}")}\n" +
        $"App version: {appVersion}\n\n" +
        "This bundle contains local, user-requested diagnostics only. It is not uploaded automatically.\n\n" +
        $"- Telemetry windows: {windows.Count}\n" +
        $"- Process instances: {processInstanceCount}\n" +
        $"- Merged metrics: {metricCount}\n" +
        $"- Relevant log records: {logCount}\n" +
        $"- Dropped windows: {droppedWindowCount}\n" +
        $"- Collection degraded: {degraded}\n";

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static DateTime FloorWindow(DateTime timestampUtc) =>
        new(timestampUtc.Year, timestampUtc.Month, timestampUtc.Day, timestampUtc.Hour, timestampUtc.Minute, 0, DateTimeKind.Utc);

    private ProcessCpuSample CaptureCpuSample()
    {
        using var process = Process.GetCurrentProcess();
        return new ProcessCpuSample(_timeProvider.GetTimestamp(), process.TotalProcessorTime, process.WorkingSet64);
    }

    private string GetTelemetryDirectory() =>
        _pathResolver.ResolvePath(Path.Combine(_directories.RootDirectoryPath, "Telemetry"));

    private static string ResolveVersion() =>
        typeof(LocalPerformanceTelemetryStore).Assembly.GetName().Version?.ToString() ?? "unknown";

    private void ApplyRetention(string directory, DateTimeOffset now)
    {
        directory = _pathResolver.ResolvePath(directory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var files = Directory.EnumerateFiles(directory, "novelspeaker-telemetry-*.jsonl")
            .Select(path => new FileInfo(_pathResolver.ResolvePath(path)))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        var cutoff = now.UtcDateTime - Retention;
        foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff).ToArray())
        {
            file.Delete();
            files.Remove(file);
        }

        var totalBytes = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (totalBytes <= MaxDirectoryBytes)
            {
                break;
            }

            totalBytes -= file.Length;
            file.Delete();
        }
    }

    private void TryApplyRetention(DateTimeOffset now)
    {
        try
        {
            ApplyRetention(GetTelemetryDirectory(), now);
        }
        catch
        {
            Volatile.Write(ref _degraded, true);
        }
    }

    private sealed record WriterItem(TelemetryWindowRecord? WindowRecord, TaskCompletionSource? FlushCompletion)
    {
        public static WriterItem Window(TelemetryWindowRecord record) => new(record, null);
        public static WriterItem Flush(TaskCompletionSource completion) => new(null, completion);
    }

    private sealed record ProcessCpuSample(
        long Timestamp,
        TimeSpan TotalProcessorTime,
        long WorkingSetBytes);

    private sealed class TelemetryWindowRecord
    {
        public TelemetryWindowRecord(
            DateTimeOffset windowStartUtc,
            DateTimeOffset windowEndUtc,
            string appVersion,
            string? processInstanceId)
        {
            SchemaVersion = WindowSchemaVersion;
            WindowStartUtc = windowStartUtc;
            WindowEndUtc = windowEndUtc;
            AppVersion = appVersion;
            ProcessInstanceId = processInstanceId;
        }

        public int SchemaVersion { get; set; }
        public DateTimeOffset WindowStartUtc { get; set; }
        public DateTimeOffset WindowEndUtc { get; set; }
        public string AppVersion { get; set; }
        public string? ProcessInstanceId { get; set; }
        public Dictionary<string, MetricAggregateRecord> Metrics { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class MetricAggregateRecord
    {
        public MetricAggregateRecord(
            string name,
            string type,
            string unit,
            SortedDictionary<string, string> tags,
            double[] histogramBuckets,
            long[] buckets)
        {
            Name = name;
            Type = type;
            Unit = unit;
            Tags = tags;
            HistogramBuckets = histogramBuckets;
            Buckets = buckets;
        }

        public string Name { get; set; }
        public string Type { get; set; }
        public string Unit { get; set; }
        public SortedDictionary<string, string> Tags { get; set; }
        public long Count { get; set; }
        public double Sum { get; set; }
        public double Min { get; set; } = double.MaxValue;
        public double Max { get; set; } = double.MinValue;
        public double Last { get; set; }
        public double[] HistogramBuckets { get; set; }
        public long[] Buckets { get; set; }
    }

    private sealed class ExportMetric
    {
        public ExportMetric(
            string appVersion,
            string dateUtc,
            string name,
            string type,
            string unit,
            SortedDictionary<string, string> tags,
            double[] histogramBuckets)
        {
            AppVersion = appVersion;
            DateUtc = dateUtc;
            Name = name;
            Type = type;
            Unit = unit;
            Tags = tags;
            HistogramBuckets = histogramBuckets;
        }

        public string AppVersion { get; }
        public string DateUtc { get; }
        public string Name { get; }
        public string Type { get; }
        public string Unit { get; }
        public SortedDictionary<string, string> Tags { get; }
        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double Min { get; private set; } = double.MaxValue;
        public double Max { get; private set; } = double.MinValue;
        public double Last { get; private set; }
        public double[] HistogramBuckets { get; }
        public long[] Buckets => UnsafeBuckets;
        public double? ApproximateP50 { get; private set; }
        public double? ApproximateP95 { get; private set; }
        public double? ApproximateP99 { get; private set; }

        public void Add(MetricAggregateRecord source)
        {
            Count += source.Count;
            Sum += source.Sum;
            Min = Math.Min(Min, source.Min);
            Max = Math.Max(Max, source.Max);
            Last = source.Last;
            if (!string.Equals(Type, PerformanceMetricType.Histogram.ToString(), StringComparison.Ordinal))
            {
                return;
            }

            if (Buckets.Length == 0)
            {
                var resized = UnsafeBuckets;
                Array.Resize(ref resized, source.Buckets.Length);
                UnsafeBuckets = resized;
            }

            for (var i = 0; i < source.Buckets.Length; i++)
            {
                UnsafeBuckets[i] += source.Buckets[i];
            }

            ApproximateP50 = Percentile(0.50);
            ApproximateP95 = Percentile(0.95);
            ApproximateP99 = Percentile(0.99);
        }

        private long[] UnsafeBuckets { get; set; } = [];

        private double Percentile(double percentile)
        {
            var target = Math.Max(1L, (long)Math.Ceiling(Count * percentile));
            var cumulative = 0L;
            for (var i = 0; i < UnsafeBuckets.Length; i++)
            {
                cumulative += UnsafeBuckets[i];
                if (cumulative >= target)
                {
                    return i < HistogramBuckets.Length ? HistogramBuckets[i] : HistogramBuckets[^1];
                }
            }

            return HistogramBuckets.Length == 0 ? 0d : HistogramBuckets[^1];
        }
    }

    private static class MetricKey
    {
        public static string Create(PerformanceMetricDefinition definition, IReadOnlyDictionary<string, string>? tags) =>
            Create(definition.Name, definition.Type.ToString(), definition.Unit, tags);

        public static string Create(string name, string type, string unit, IReadOnlyDictionary<string, string>? tags)
        {
            var suffix = tags is null
                ? string.Empty
                : string.Join(";", tags.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
            return $"{name}|{type}|{unit}|{suffix}";
        }
    }
}
