using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Application.Speech.Security;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Writes redacted structured production logs through one bounded background writer.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    public const long DefaultMaxFileBytes = 8L * 1024 * 1024;
    public const long DefaultMaxTotalBytes = 64L * 1024 * 1024;
    public const int DefaultQueueCapacity = 512;
    public const int DefaultRetentionDays = 30;
    private const int BatchSize = 32;
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly Channel<LogWorkItem> _highPriorityQueue;
    private readonly Channel<LogWorkItem> _normalPriorityQueue;
    private readonly SemaphoreSlim _queueSignal;
    private readonly string _logDirectoryPath;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly long _maxFileBytes;
    private readonly long _maxTotalBytes;
    private readonly TimeSpan _retention;
    private readonly IObservabilityContextAccessor _contextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writerTask;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private long _sequence;
    private long _droppedRecordCount;
    private long _writeFailureCount;
    private int _degraded;
    private int _disposed;

    public RollingFileLoggerProvider(
        IAppDataDirectoryProvider directories,
        IObservabilityContextAccessor? contextAccessor = null,
        long maxFileBytes = DefaultMaxFileBytes,
        long maxTotalBytes = DefaultMaxTotalBytes,
        int queueCapacity = DefaultQueueCapacity,
        int retentionDays = DefaultRetentionDays,
        TimeProvider? timeProvider = null,
        IAppStoragePathResolver? pathResolver = null)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTotalBytes, maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionDays, 1);

        _pathResolver = pathResolver ?? new AppStoragePathResolver(directories);
        _logDirectoryPath = _pathResolver.ResolvePath(directories.LogsDirectoryPath);
        _maxFileBytes = maxFileBytes;
        _maxTotalBytes = maxTotalBytes;
        _retention = TimeSpan.FromDays(retentionDays);
        _contextAccessor = contextAccessor ?? new ObservabilityContextAccessor();
        _timeProvider = timeProvider ?? TimeProvider.System;
        var highPriorityCapacity = Math.Max(1, queueCapacity / 4);
        var normalPriorityCapacity = queueCapacity - highPriorityCapacity;
        _highPriorityQueue = CreateQueue(highPriorityCapacity);
        _normalPriorityQueue = CreateQueue(normalPriorityCapacity);
        _queueSignal = new SemaphoreSlim(0, highPriorityCapacity + normalPriorityCapacity + 1);
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public long DroppedRecordCount => Interlocked.Read(ref _droppedRecordCount);

    public long WriteFailureCount => Interlocked.Read(ref _writeFailureCount);

    public bool IsDegraded => WriteFailureCount != 0 || Volatile.Read(ref _degraded) != 0;

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        return new RollingFileLogger(this, categoryName);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(FlushTimeout);
        try
        {
            await _highPriorityQueue.Writer
                .WriteAsync(LogWorkItem.CreateFlush(completion), timeoutCancellation.Token)
                .ConfigureAwait(false);
            SignalQueue();
            await completion.Task.WaitAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _degraded, 1);
        }
    }

    public void Dispose() => _ = ObserveDisposeAsync(StartDispose());

    public ValueTask DisposeAsync() => new(StartDispose());

    private Task StartDispose()
    {
        lock (_disposeSync)
        {
            if (_disposeTask is not null)
            {
                return _disposeTask;
            }

            Interlocked.Exchange(ref _disposed, 1);
            _highPriorityQueue.Writer.TryComplete();
            _normalPriorityQueue.Writer.TryComplete();
            SignalQueue();
            _disposeTask = DisposeWriterAsync();
            return _disposeTask;
        }
    }

    private async Task DisposeWriterAsync()
    {
        var writerTimedOut = false;
        try
        {
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            writerTimedOut = true;
            Interlocked.Exchange(ref _degraded, 1);
            _shutdown.Cancel();
            _ = DisposeCancellationWhenWriterStopsAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!writerTimedOut)
            {
                _shutdown.Dispose();
            }
        }
    }

    private async Task DisposeCancellationWhenWriterStopsAsync()
    {
        try
        {
            await _writerTask.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private static async Task ObserveDisposeAsync(Task disposeTask)
    {
        try
        {
            await disposeTask.ConfigureAwait(false);
        }
        catch
        {
            // ILoggerProvider.Dispose cannot report failures to its caller.
        }
    }

    private void Enqueue<TState>(
        LogLevel logLevel,
        EventId eventId,
        string categoryName,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (Volatile.Read(ref _disposed) != 0 || logLevel is LogLevel.None or LogLevel.Trace or LogLevel.Debug)
        {
            return;
        }

        LogWorkItem workItem;
        try
        {
            var definition = LogEventRegistry.Resolve(eventId, categoryName);
            var structuredException = exception is not null
                ? SanitizedExceptionDetails.Create(exception)
                : state is IStructuredExceptionLogState exceptionState
                    ? exceptionState.ExceptionDetails
                    : null;
            workItem = LogWorkItem.CreateRecord(new LogRecord(
                SchemaVersion: 1,
                TimestampUtc: _timeProvider.GetUtcNow().ToUniversalTime(),
                Sequence: Interlocked.Increment(ref _sequence),
                Level: logLevel.ToString(),
                EventId: definition.Id,
                EventName: definition.EventName,
                Category: definition.Category,
                Operation: definition.Operation?.Id.Value ?? "unknown",
                Message: SanitizeText(formatter(state, exception)),
                AppVersion: ResolveAppVersion(),
                Correlation: _contextAccessor.Current,
                Properties: ExtractProperties(state) is { Count: > 0 } properties ? properties : null,
                Exception: structuredException is null
                    ? null
                    : StructuredException.Create(structuredException)));
        }
        catch
        {
            Interlocked.Increment(ref _writeFailureCount);
            return;
        }

        var queue = logLevel >= LogLevel.Warning ? _highPriorityQueue : _normalPriorityQueue;
        if (!queue.Writer.TryWrite(workItem))
        {
            Interlocked.Increment(ref _droppedRecordCount);
            return;
        }

        SignalQueue();
    }

    private void SignalQueue()
    {
        try
        {
            _queueSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A stale wake-up is sufficient; the queue state is authoritative.
        }
    }

    private async Task WriterLoopAsync()
    {
        FileState? fileState = null;
        var pendingRecordCount = 0;
        var highPriorityStreak = 0;
        try
        {
            while (await WaitForWorkAsync().ConfigureAwait(false))
            {
                while (TryReadNext(ref highPriorityStreak, out var workItem))
                {
                    if (workItem.Record is not null)
                    {
                        try
                        {
                            fileState = WriteRecord(fileState, workItem.Record);
                            pendingRecordCount++;
                        }
                        catch
                        {
                            Interlocked.Increment(ref _writeFailureCount);
                            ReleaseFileState(ref fileState);
                            pendingRecordCount = 0;
                        }
                    }

                    if (workItem.FlushCompletion is not null || pendingRecordCount >= BatchSize)
                    {
                        FlushFileState(ref fileState);
                        pendingRecordCount = 0;
                    }

                    if (workItem.FlushCompletion is not null)
                    {
                        while (TryReadNext(ref highPriorityStreak, out var pendingWorkItem))
                        {
                            ProcessWorkItem(
                                pendingWorkItem,
                                ref fileState,
                                ref pendingRecordCount);
                        }

                        FlushFileState(ref fileState);
                        pendingRecordCount = 0;
                        workItem.FlushCompletion.TrySetResult();
                    }
                    else
                    {
                        workItem.FlushCompletion?.TrySetResult();
                    }
                }

                if (AreQueuesCompleted())
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            ReleaseFileState(ref fileState);
        }
    }

    private async Task<bool> WaitForWorkAsync()
    {
        try
        {
            await _queueSignal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return false;
        }
    }

    private bool TryReadNext(ref int highPriorityStreak, out LogWorkItem workItem)
    {
        if (highPriorityStreak < BatchSize &&
            _highPriorityQueue.Reader.TryRead(out var highPriorityWorkItem) &&
            highPriorityWorkItem is not null)
        {
            workItem = highPriorityWorkItem;
            highPriorityStreak++;
            return true;
        }

        if (_normalPriorityQueue.Reader.TryRead(out var normalPriorityWorkItem) &&
            normalPriorityWorkItem is not null)
        {
            workItem = normalPriorityWorkItem;
            highPriorityStreak = 0;
            return true;
        }

        if (_highPriorityQueue.Reader.TryRead(out highPriorityWorkItem) && highPriorityWorkItem is not null)
        {
            workItem = highPriorityWorkItem;
            highPriorityStreak = 1;
            return true;
        }

        workItem = null!;
        return false;
    }

    private bool AreQueuesCompleted() =>
        _highPriorityQueue.Reader.Completion.IsCompleted &&
        _normalPriorityQueue.Reader.Completion.IsCompleted;

    private void ProcessWorkItem(
        LogWorkItem workItem,
        ref FileState? fileState,
        ref int pendingRecordCount)
    {
        if (workItem.Record is not null)
        {
            try
            {
                fileState = WriteRecord(fileState, workItem.Record);
                pendingRecordCount++;
            }
            catch
            {
                Interlocked.Increment(ref _writeFailureCount);
                ReleaseFileState(ref fileState);
                pendingRecordCount = 0;
            }
        }

        if (workItem.FlushCompletion is not null || pendingRecordCount >= BatchSize)
        {
            FlushFileState(ref fileState);
            pendingRecordCount = 0;
        }

        workItem.FlushCompletion?.TrySetResult();
    }

    private void FlushFileState(ref FileState? fileState)
    {
        try
        {
            fileState?.Writer.Flush();
        }
        catch
        {
            Interlocked.Increment(ref _writeFailureCount);
            ReleaseFileState(ref fileState);
        }
    }

    private static Channel<LogWorkItem> CreateQueue(int capacity) =>
        Channel.CreateBounded<LogWorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private void ReleaseFileState(ref FileState? fileState)
    {
        var current = fileState;
        fileState = null;
        try
        {
            current?.Dispose();
        }
        catch
        {
            Interlocked.Increment(ref _writeFailureCount);
        }
    }

    private FileState WriteRecord(FileState? state, LogRecord record)
    {
        var date = record.TimestampUtc.UtcDateTime.Date;
        var json = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(json);
        var replacement = state is null || state.Date != date || state.Length + byteCount > _maxFileBytes;
        var target = replacement ? OpenFile(date, byteCount, state?.Path) : state!;

        try
        {
            target.Writer.Write(json);
            target.Length += byteCount;
            ApplyRetention(date, target.Path, state?.Path);
        }
        catch
        {
            if (replacement)
            {
                ReleaseFileState(ref target);
            }

            throw;
        }

        if (replacement)
        {
            ReleaseFileState(ref state);
        }

        return target;
    }

    private FileState OpenFile(DateTime date, long pendingByteCount, string? excludedPath = null)
    {
        var logDirectoryPath = _pathResolver.ResolvePath(_logDirectoryPath);
        Directory.CreateDirectory(logDirectoryPath);
        var index = 0;
        string path;
        do
        {
            path = _pathResolver.ResolvePath(Path.Combine(
                logDirectoryPath,
                $"novelspeaker-{date:yyyyMMdd}-{index:000}.jsonl"));
            index++;
        }
        while (File.Exists(path) &&
               (path.Equals(excludedPath, StringComparison.OrdinalIgnoreCase) ||
                new FileInfo(path).Length + pendingByteCount > _maxFileBytes));

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: false);
        return new FileState(
            date,
            path,
            new StreamWriter(stream, new System.Text.UTF8Encoding(false)),
            stream.Length);
    }

    private void ApplyRetention(DateTime currentDate, string activePath, string? previousPath = null)
    {
        var logDirectoryPath = _pathResolver.ResolvePath(_logDirectoryPath);
        var files = Directory.EnumerateFiles(logDirectoryPath, "novelspeaker-*.jsonl")
            .Select(path => new FileInfo(_pathResolver.ResolvePath(path)))
            .OrderBy(info => info.LastWriteTimeUtc)
            .ToList();
        var cutoff = currentDate - _retention;
        foreach (var file in files.Where(file =>
                     file.LastWriteTimeUtc < cutoff &&
                     !IsProtectedPath(file.FullName, activePath, previousPath)))
        {
            TryDelete(file.FullName);
        }

        files = files.Where(file => file.Exists).ToList();
        var totalBytes = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (totalBytes <= _maxTotalBytes)
            {
                break;
            }

            if (!IsProtectedPath(file.FullName, activePath, previousPath) && TryDelete(file.FullName))
            {
                totalBytes -= file.Length;
            }
        }
    }

    private static bool IsProtectedPath(string path, string activePath, string? previousPath) =>
        path.Equals(activePath, StringComparison.OrdinalIgnoreCase) ||
        (previousPath is not null && path.Equals(previousPath, StringComparison.OrdinalIgnoreCase));

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(_pathResolver.ResolvePath(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, object?> ExtractProperties<TState>(TState state)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is not IEnumerable<KeyValuePair<string, object?>> values)
        {
            return properties;
        }

        foreach (var (key, value) in values)
        {
            if (key.Equals("{OriginalFormat}", StringComparison.Ordinal))
            {
                continue;
            }

            properties[key] = IsForbiddenProperty(key)
                ? "***"
                : value switch
                {
                    null => null,
                    bool boolean => boolean,
                    byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToInt64(value),
                    float or double or decimal => Convert.ToDouble(value),
                    Enum enumValue => enumValue.ToString(),
                    _ => SanitizeText(value.ToString() ?? string.Empty)
                };
        }

        return properties;
    }

    private static bool IsForbiddenProperty(string key) =>
        SensitiveDataRedactor.IsSensitiveKey(key) ||
        SensitiveDataRedactor.IsContentKey(key) ||
        key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("url", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("query", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("title", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("chapter", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("book", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeText(string value) => LogTextSanitizer.Sanitize(value);

    private static string ResolveAppVersion() =>
        typeof(RollingFileLoggerProvider).Assembly.GetName().Version?.ToString() ?? "unknown";

    private sealed class RollingFileLogger(RollingFileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Information and <= LogLevel.Critical;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Enqueue(logLevel, eventId, categoryName, state, exception, formatter);
            }
        }
    }

    private sealed record LogWorkItem(LogRecord? Record, TaskCompletionSource? FlushCompletion)
    {
        public static LogWorkItem CreateRecord(LogRecord record) => new(record, null);

        public static LogWorkItem CreateFlush(TaskCompletionSource completion) => new(null, completion);
    }

    private sealed class FileState : IDisposable
    {
        public FileState(DateTime date, string path, StreamWriter writer, long length)
        {
            Date = date;
            Path = path;
            Writer = writer;
            Length = length;
        }

        public DateTime Date { get; }

        public string Path { get; }

        public StreamWriter Writer { get; }

        public long Length { get; set; }

        public void Dispose()
        {
            Writer.Dispose();
        }
    }

    private sealed record LogRecord(
        int SchemaVersion,
        DateTimeOffset TimestampUtc,
        long Sequence,
        string Level,
        int EventId,
        string EventName,
        string Category,
        string Operation,
        string Message,
        string AppVersion,
        [property: JsonIgnore] CorrelationContext Correlation,
        IReadOnlyDictionary<string, object?>? Properties,
        StructuredException? Exception)
    {
        [JsonIgnore]
        public CorrelationContext Context => Correlation;

        public string ProcessInstanceId => Context.ProcessInstanceId;

        public string? DiagnosticSessionId => Context.DiagnosticSessionId;

        public string? ActivityId => Context.ActivityId;
    }

    private sealed record StructuredException(
        string Type,
        int HResult,
        string? Message,
        string? StackTrace,
        IReadOnlyList<StructuredException> Children,
        bool Truncated)
    {
        public static StructuredException Create(SanitizedExceptionDetails details) => new(
            details.Type,
            details.HResult,
            details.Message,
            details.StackTrace,
            details.Children.Select(Create).ToArray(),
            details.Truncated);
    }
}
