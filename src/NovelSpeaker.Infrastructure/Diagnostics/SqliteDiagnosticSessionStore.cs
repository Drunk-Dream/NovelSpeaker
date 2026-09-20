using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Owns the one active diagnostic session and persists it as a self-contained SQLite file.
/// </summary>
public sealed class SqliteDiagnosticSessionStore : IDiagnosticSessionService, IObservabilityConsumer, IAsyncDisposable
{
    internal const int CurrentSchemaVersion = 3;
    private const int BatchSize = 64;
    private const int QueueCapacity = 256;
    private const long ReserveBytes = 16 * 1024;
    private const int MarkerMaxBytes = 256;
    private const string ActiveState = "active";
    private const string EndedState = "ended";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly IObservabilityContextAccessor _contextAccessor;
    private readonly IAppSettingsService _settings;
    private readonly TimeProvider _timeProvider;
    private readonly DiagnosticRegistry _registry;
    private readonly IDiagnosticFailureReporter? _failureReporter;
    private readonly Channel<WriteRequest> _queue;
    private readonly int _ordinaryQueueLimit;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _gate = new();
    private readonly Task _writerTask;
    private ActiveSession? _active;
    private DiagnosticSessionSnapshot? _lastEnded;
    private string? _lastEndedPath;
    private int _degraded;
    private int _disposed;

    public SqliteDiagnosticSessionStore(
        IAppDataDirectoryProvider directories,
        IObservabilityContextAccessor contextAccessor,
        TimeProvider timeProvider,
        IAppSettingsService settings,
        IAppStoragePathResolver pathResolver,
        DiagnosticRegistry? registry = null,
        IDiagnosticFailureReporter? failureReporter = null)
        : this(directories, contextAccessor, timeProvider, settings, pathResolver, registry, QueueCapacity, failureReporter)
    {
    }

    internal SqliteDiagnosticSessionStore(
        IAppDataDirectoryProvider directories,
        IObservabilityContextAccessor contextAccessor,
        TimeProvider timeProvider,
        IAppSettingsService settings,
        IAppStoragePathResolver pathResolver,
        DiagnosticRegistry? registry,
        int queueCapacity,
        IDiagnosticFailureReporter? failureReporter = null)
    {
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _registry = registry ?? DiagnosticRegistry.Default;
        _failureReporter = failureReporter;
        if (queueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        }

        var criticalReserve = Math.Max(1, queueCapacity / 4);
        _ordinaryQueueLimit = Math.Max(0, queueCapacity - criticalReserve);
        _queue = Channel.CreateBounded<WriteRequest>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public DiagnosticSessionSnapshot? Current => SnapshotActive();

    public DiagnosticSessionSnapshot? LastEnded
    {
        get
        {
            lock (_gate)
            {
                return _lastEnded;
            }
        }
    }

    internal string? LastEndedPath
    {
        get
        {
            lock (_gate)
            {
                return _lastEndedPath;
            }
        }
    }

    internal bool IsDegraded => Volatile.Read(ref _degraded) != 0;

    public async Task<DiagnosticSessionSnapshot> StartAsync(
        DiagnosticSessionStartOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_active is not null)
                {
                    throw new InvalidOperationException("已有诊断会话正在采集。");
                }
            }

            await _directories.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            var sessionId = CreateToken();
            var startedAt = _timeProvider.GetUtcNow().ToUniversalTime();
            var path = _pathResolver.ResolvePath(
                Path.Combine(_directories.DiagnosticsDirectoryPath, $"session-{sessionId}.nsdiag"));

            var processInstanceId = _contextAccessor.Current.ProcessInstanceId;
            await CreateSessionFileAsync(
                path,
                sessionId,
                startedAt,
                options.HardCapBytes,
                processInstanceId,
                cancellationToken).ConfigureAwait(false);

            try
            {
                await WriteMarkerAsync(Path.GetFileName(path), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryDeleteTrustedFile(path);
                throw;
            }

            var active = new ActiveSession(
                sessionId,
                path,
                startedAt,
                options.HardCapBytes,
                processInstanceId,
                0,
                false,
                false);
            lock (_gate)
            {
                _active = active;
            }

            _contextAccessor.SetDiagnosticSession(sessionId);
            return active.ToSnapshot();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportFailure(DiagnosticFailureOperation.SessionStart, DiagnosticFailureStage.CreateSession, exception);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<DiagnosticSessionSnapshot?> RecoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_active is not null)
                {
                    return _active.ToSnapshot();
                }
            }

            string? fileName;
            try
            {
                fileName = ReadMarkerFileName();
            }
            catch (Exception exception)
            {
                ReportFailure(DiagnosticFailureOperation.SessionRecover, DiagnosticFailureStage.ReadSessionMarker, exception);
                TryDeleteTrustedFile(_directories.ActiveDiagnosticSessionMarkerPath);
                return null;
            }

            if (fileName is null)
            {
                return null;
            }

            var path = ResolveDiagnosticFile(fileName);
            if (path is null || !File.Exists(path))
            {
                ReportFailure(
                    DiagnosticFailureOperation.SessionRecover,
                    DiagnosticFailureStage.ReadSessionMarker,
                    new InvalidDataException("诊断会话标记指向不可用的会话文件。"));
                TryDeleteTrustedFile(_directories.ActiveDiagnosticSessionMarkerPath);
                return null;
            }

            try
            {
                await using var connection = await OpenConnectionAsync(path, cancellationToken).ConfigureAwait(false);
                await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
                var session = await ReadSessionAsync(connection, cancellationToken).ConfigureAwait(false);
                if (session is null || !string.Equals(session.State, ActiveState, StringComparison.Ordinal))
                {
                    try
                    {
                        await CheckpointAsync(connection, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // Keep the marker so the next process can retry making the ended file self-contained.
                        Volatile.Write(ref _degraded, 1);
                        ReportFailure(DiagnosticFailureOperation.SessionRecover, DiagnosticFailureStage.RecoverSession, exception);
                        return null;
                    }

                    TryDeleteTrustedFile(_directories.ActiveDiagnosticSessionMarkerPath);
                    return session is null ? null : ToSnapshot(session, _contextAccessor.Current.ProcessInstanceId);
                }

                await ConfigurePageLimitAsync(connection, session.HardCapBytes, cancellationToken).ConfigureAwait(false);

                var processInstanceId = _contextAccessor.Current.ProcessInstanceId;
                var now = _timeProvider.GetUtcNow().ToUniversalTime();
                await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    var previous = await ReadLatestProcessAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                    if (previous is not null && previous.EndedAtUtc is null)
                    {
                        await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE Processes SET EndedAtUtc = $endedAtUtc, EndReason = $reason WHERE ProcessInstanceId = $id;",
                            cancellationToken,
                            ("$endedAtUtc", now.ToString("O", CultureInfo.InvariantCulture)),
                            ("$reason", "unexpected"),
                            ("$id", previous.ProcessInstanceId));
                        await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE Sessions SET EndedUnexpectedly = 1 WHERE SessionId = $id;",
                            cancellationToken,
                            ("$id", session.SessionId));
                        session = session with { EndedUnexpectedly = true };
                    }

                    await InsertProcessAsync(
                        connection,
                        transaction,
                        processInstanceId,
                        now,
                        cancellationToken);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                var active = new ActiveSession(
                    session.SessionId,
                    path,
                    session.StartedAtUtc,
                    session.HardCapBytes,
                    processInstanceId,
                    session.RecordedBytes,
                    session.CaptureStopped,
                    session.EndedUnexpectedly,
                    session.CaptureStoppedReason);
                lock (_gate)
                {
                    _active = active;
                }

                _contextAccessor.SetDiagnosticSession(session.SessionId);
                return active.ToSnapshot();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A damaged marker or session must never prevent the main application from starting.
                Volatile.Write(ref _degraded, 1);
                ReportFailure(DiagnosticFailureOperation.SessionRecover, DiagnosticFailureStage.RecoverSession, exception);
                return null;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<DiagnosticSessionSnapshot> EndAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active;
            lock (_gate)
            {
                active = _active ?? throw new InvalidOperationException("当前没有正在采集的诊断会话。");
                active.AcceptingWrites = false;
            }

            await FlushPathAsync(active.Path, cancellationToken).ConfigureAwait(false);
            var endedAt = _timeProvider.GetUtcNow().ToUniversalTime();
            await using (var connection = await OpenConnectionAsync(active.Path, cancellationToken).ConfigureAwait(false))
            {
                await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        "UPDATE Sessions SET State = $state, EndedAtUtc = $endedAtUtc, RecordedBytes = $recordedBytes, CaptureStopped = $captureStopped, CaptureStoppedReason = $captureStoppedReason WHERE SessionId = $id;",
                        cancellationToken,
                        ("$state", EndedState),
                        ("$endedAtUtc", endedAt.ToString("O", CultureInfo.InvariantCulture)),
                        ("$recordedBytes", active.RecordedBytes),
                        ("$captureStopped", active.CaptureStopped ? 1 : 0),
                        ("$captureStoppedReason", active.CaptureStoppedReason),
                        ("$id", active.SessionId));
                    await ExecuteAsync(
                        connection,
                        transaction,
                        "UPDATE Processes SET EndedAtUtc = $endedAtUtc, EndReason = $reason WHERE ProcessInstanceId = $id AND EndedAtUtc IS NULL;",
                        cancellationToken,
                        ("$endedAtUtc", endedAt.ToString("O", CultureInfo.InvariantCulture)),
                        ("$reason", "session-ended"),
                        ("$id", active.ProcessInstanceId));
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    await CheckpointAsync(connection, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Keep the in-memory active state and marker so a later End can retry the checkpoint.
                    Volatile.Write(ref _degraded, 1);
                    throw new IOException("诊断会话已结束，但仍需完成 SQLite checkpoint。", exception);
                }
            }

            var result = active.ToSnapshot(endedAt, DiagnosticSessionState.Ended);
            lock (_gate)
            {
                _active = null;
                _lastEnded = result;
                _lastEndedPath = active.Path;
            }

            _contextAccessor.SetDiagnosticSession(null);
            TryDeleteMarker(active.Path);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportFailure(DiagnosticFailureOperation.SessionEnd, DiagnosticFailureStage.EndSession, exception);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task NotifyProcessShutdownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession? active;
            lock (_gate)
            {
                active = _active;
                if (active is not null)
                {
                    active.AcceptingWrites = false;
                }
            }

            if (active is null)
            {
                return;
            }

            await FlushPathAsync(active.Path, cancellationToken).ConfigureAwait(false);
            var endedAt = _timeProvider.GetUtcNow().ToUniversalTime();
            try
            {
                await using var connection = await OpenConnectionAsync(active.Path, cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(
                    connection,
                    transaction,
                    "UPDATE Sessions SET RecordedBytes = $recordedBytes, CaptureStopped = $captureStopped, CaptureStoppedReason = $captureStoppedReason WHERE SessionId = $id;",
                    cancellationToken,
                    ("$recordedBytes", active.RecordedBytes),
                    ("$captureStopped", active.CaptureStopped ? 1 : 0),
                    ("$captureStoppedReason", active.CaptureStoppedReason),
                    ("$id", active.SessionId));
                await ExecuteAsync(
                    connection,
                    transaction,
                    "UPDATE Processes SET EndedAtUtc = $endedAtUtc, EndReason = $reason WHERE ProcessInstanceId = $id AND EndedAtUtc IS NULL;",
                    cancellationToken,
                    ("$endedAtUtc", endedAt.ToString("O", CultureInfo.InvariantCulture)),
                    ("$reason", "normal-exit"),
                    ("$id", active.ProcessInstanceId));
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                await CheckpointAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_active, active))
                    {
                        _active = null;
                    }
                }

                _contextAccessor.SetDiagnosticSession(null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportFailure(DiagnosticFailureOperation.SessionEnd, DiagnosticFailureStage.EndSession, exception);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task AddAttachmentAsync(
        DiagnosticAttachment attachment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(attachment.AttachmentId) ||
            attachment.AttachmentId.Any(character => !((character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("附件标识必须是稳定 token。", nameof(attachment));
        }

        var content = attachment.Content?.ToArray()
            ?? throw new ArgumentException("附件内容不能为空。", nameof(attachment));
        if (content.Length == 0 ||
            attachment.PixelWidth <= 0 ||
            attachment.PixelHeight <= 0 ||
            !string.Equals(attachment.MimeType, "image/png", StringComparison.Ordinal))
        {
            throw new ArgumentException("只支持有效的 PNG 窗口截图附件。", nameof(attachment));
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active;
            TaskCompletionSource<bool> completion;
            lock (_gate)
            {
                active = _active ?? throw new InvalidOperationException("只有活动诊断会话可以保存附件。");
                if (!active.AcceptingWrites)
                {
                    throw new InvalidOperationException("诊断会话正在结束，无法保存附件。");
                }

                var estimate = content.LongLength + 512;
                if (active.CaptureStopped || !CanAccept(active, estimate))
                {
                    MarkCaptureStoppedLocked(active, "hard-cap");
                    throw new InvalidOperationException("诊断会话已达到容量上限。");
                }

                active.RecordedBytes = Math.Min(active.HardCapBytes, active.RecordedBytes + estimate);
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var recordedBytes = active.RecordedBytes;
                var request = new WriteRequest(
                    active.Path,
                    (connection, transaction) =>
                    {
                        InsertAttachment(connection, transaction, attachment, content, active.ProcessInstanceId);
                        UpdateSessionMetadata(connection, transaction, active.SessionId, recordedBytes, active.CaptureStopped, active.CaptureStoppedReason);
                    },
                    completion,
                    false);
                if (!TryWriteRequestLocked(active, request, isCritical: true))
                {
                    active.RecordedBytes -= estimate;
                    throw new IOException("诊断队列繁忙，截图未能加入会话。");
                }
            }

            if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false))
            {
                throw new IOException("诊断附件未能写入会话文件。");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportFailure(DiagnosticFailureOperation.WindowCapture, DiagnosticFailureStage.SaveAttachment, exception);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<bool> RecordProblemMarkerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await RecordProblemMarkerCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportFailure(DiagnosticFailureOperation.ProblemMarker, DiagnosticFailureStage.RecordMarker, exception);
            throw;
        }
    }

    private async Task<bool> RecordProblemMarkerCoreAsync(CancellationToken cancellationToken)
    {
        var definition = _registry.Get(new DiagnosticDefinitionId("diagnostics.problem_marker"));
        var source = definition.Fields.Single(field => field.Name == "source");
        var marker = new DiagnosticEvent(
            definition,
            definition.CreateFields(DiagnosticFieldValue.Enum(source, "user")),
            _contextAccessor.Current,
            _timeProvider.GetUtcNow());
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryEnqueueDiagnosticEvent(marker, force: false, completion: completion))
        {
            ReportFailure(
                DiagnosticFailureOperation.ProblemMarker,
                DiagnosticFailureStage.RecordMarker,
                new IOException("问题标记未能加入诊断会话。"));
            return false;
        }

        bool markerRecorded;
        try
        {
            markerRecorded = await completion.Task
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ReportFailure(
                DiagnosticFailureOperation.ProblemMarker,
                DiagnosticFailureStage.RecordMarker,
                new TimeoutException("问题标记写入诊断会话超时。"));
            return false;
        }

        if (!markerRecorded)
        {
            ReportFailure(
                DiagnosticFailureOperation.ProblemMarker,
                DiagnosticFailureStage.RecordMarker,
                new IOException("问题标记未能写入诊断会话。"));
            return false;
        }

        var snapshotDefinition = _registry.Get(new DiagnosticDefinitionId("diagnostics.active_activities"));
        var countField = snapshotDefinition.Fields.Single(field => field.Name == "activeActivityCount");
        int activeCount;
        lock (_gate)
        {
            activeCount = _active?.ActiveActivityIds.Count ?? 0;
        }

        Record(
            snapshotDefinition,
            snapshotDefinition.CreateFields(DiagnosticFieldValue.Integer(countField, activeCount)));
        EnqueueResourceSample("problem-marker");
        return true;
    }

    public string GetOrCreateAnonymousObjectToken(string objectType, string objectIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectIdentity);
        if (objectType.Any(character => !((character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("对象类型必须是稳定的小写 token。", nameof(objectType));
        }

        lock (_gate)
        {
            if (_active is null)
            {
                throw new InvalidOperationException("只有活动诊断会话可以关联匿名对象。");
            }

            if (!_active.AcceptingWrites)
            {
                throw new InvalidOperationException("诊断会话正在结束，无法关联匿名对象。");
            }

            var key = objectType + "\u001f" + objectIdentity;
            if (_active.AnonymousObjectTokens.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var token = CreateAnonymousToken(_active.SessionId, objectType, objectIdentity);
            _active.AnonymousObjectTokens.Add(key, token);

            var context = _contextAccessor.Current;
            var activityId = IsSessionContext(_active, context) ? context.ActivityId : null;
            var estimate = EstimateBytes(objectType, token) + 96;
            if (!CanAccept(_active, estimate))
            {
                MarkCaptureStoppedLocked(_active, "hard-cap");
                return token;
            }

            _active.RecordedBytes += estimate;
            var recordedBytes = _active.RecordedBytes;
            var active = _active;
            var associatedAt = _timeProvider.GetUtcNow().ToUniversalTime();
            var request = new WriteRequest(
                active.Path,
                (connection, transaction) =>
                {
                    InsertAnonymousObjectAssociation(
                        connection,
                        transaction,
                        token,
                        objectType,
                        active.ProcessInstanceId,
                        activityId,
                        associatedAt);
                    UpdateSessionMetadata(connection, transaction, active.SessionId, recordedBytes, active.CaptureStopped, active.CaptureStoppedReason);
                },
                null,
                false);
            if (!TryWriteRequestLocked(active, request, isCritical: false))
            {
                active.RecordedBytes -= estimate;
            }

            return token;
        }
    }

    public void Record(DiagnosticDefinition definition, DiagnosticFieldSet fields)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fields);
        if (!IsRegisteredDefinition(definition, fields))
        {
            return;
        }

        var diagnosticEvent = new DiagnosticEvent(
            definition,
            fields,
            _contextAccessor.Current,
            _timeProvider.GetUtcNow());
        OnDiagnosticEvent(diagnosticEvent);
    }

    public void OnOperationStarted(OperationStarted operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ActiveSession? active;
        lock (_gate)
        {
            active = _active;
            if (active is not null &&
                IsSessionContext(active, operation.Context) &&
                operation.Context.ActivityId is not null)
            {
                active.ActiveActivityIds.Add(operation.Context.ActivityId);
            }
        }

        if (active is null ||
            !IsSessionContext(active, operation.Context) ||
            operation.Context.ActivityId is null)
        {
            return;
        }

        var activityId = operation.Context.ActivityId;
        EnqueueRecord(
            active,
            EstimateBytes(operation.Operation.Id.Value, activityId),
            (connection, transaction) => InsertActivityStarted(
                connection,
                transaction,
                operation,
                operation.TimestampUtc),
            force: false);
    }

    public void OnOperationCompleted(OperationCompleted operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ActiveSession? active;
        lock (_gate)
        {
            active = _active;
            if (active is not null &&
                IsSessionContext(active, operation.Context) &&
                operation.Context.ActivityId is not null)
            {
                active.ActiveActivityIds.Remove(operation.Context.ActivityId);
            }
        }

        if (active is null ||
            !IsSessionContext(active, operation.Context) ||
            operation.Context.ActivityId is null)
        {
            return;
        }

        EnqueueRecord(
            active,
            EstimateBytes(operation.Operation.Id.Value, operation.Context.ActivityId),
            (connection, transaction) => UpdateActivityCompleted(connection, transaction, operation),
            force: false);
    }

    public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent)
    {
        TryEnqueueDiagnosticEvent(
            diagnosticEvent,
            force: diagnosticEvent.Definition.Id.Value == "diagnostics.capture_stopped");
    }

    private bool TryEnqueueDiagnosticEvent(
        DiagnosticEvent diagnosticEvent,
        bool force,
        TaskCompletionSource<bool>? completion = null)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        if (!IsRegisteredDefinition(diagnosticEvent.Definition, diagnosticEvent.Fields))
        {
            completion?.TrySetResult(false);
            return false;
        }

        ActiveSession? active;
        lock (_gate)
        {
            active = _active;
        }

        if (active is null || !IsSessionContext(active, diagnosticEvent.Context))
        {
            completion?.TrySetResult(false);
            return false;
        }

        var payload = SerializeFields(diagnosticEvent.Fields);
        var estimate = EstimateBytes(diagnosticEvent.Definition.Id.Value, payload);
        return EnqueueRecord(
            active,
            estimate,
            (connection, transaction) =>
            {
                if (diagnosticEvent.Definition.Kind == DiagnosticDefinitionKind.Snapshot)
                {
                    InsertSnapshot(connection, transaction, diagnosticEvent, payload);
                }
                else
                {
                    InsertEvent(connection, transaction, diagnosticEvent, payload);
                }
            },
            force,
            completion,
            IsHighPriorityEvent(diagnosticEvent));
    }

    internal Task FlushAsync(CancellationToken cancellationToken) =>
        FlushPathAsync(SnapshotActive()?.SessionId is { } sessionId
            ? Path.Combine(_directories.DiagnosticsDirectoryPath, $"session-{sessionId}.nsdiag")
            : null, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            await NotifyProcessShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportFailure(DiagnosticFailureOperation.SessionEnd, DiagnosticFailureStage.EndSession, exception);
            Volatile.Write(ref _degraded, 1);
        }

        Interlocked.Exchange(ref _disposed, 1);
        _queue.Writer.TryComplete();
        try
        {
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            Volatile.Write(ref _degraded, 1);
        }

        _lifecycleGate.Dispose();
    }

    private async Task CreateSessionFileAsync(
        string path,
        string sessionId,
        DateTimeOffset startedAt,
        long hardCapBytes,
        string processInstanceId,
        CancellationToken cancellationToken)
    {
        SqliteRuntimeInitializer.EnsureInitialized();
        await using var connection = await OpenConnectionAsync(path, cancellationToken).ConfigureAwait(false);
        await ConfigurePageLimitAsync(connection, hardCapBytes, cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO Sessions (SessionId, State, StartedAtUtc, EndedAtUtc, HardCapBytes, RecordedBytes, CaptureStopped, CaptureStoppedReason, EndedUnexpectedly) VALUES ($id, $state, $startedAtUtc, NULL, $hardCapBytes, 0, 0, NULL, 0);",
            cancellationToken,
            ("$id", sessionId),
            ("$state", ActiveState),
            ("$startedAtUtc", startedAt.ToString("O", CultureInfo.InvariantCulture)),
            ("$hardCapBytes", hardCapBytes));
        await InsertProcessAsync(connection, transaction, processInstanceId, startedAt, cancellationToken);
        await InsertEnvironmentAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await CheckpointAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ConfigurePageLimitAsync(
        SqliteConnection connection,
        long hardCapBytes,
        CancellationToken cancellationToken)
    {
        await using var pageSizeCommand = connection.CreateCommand();
        pageSizeCommand.CommandText = "PRAGMA page_size;";
        var pageSize = Convert.ToInt64(
            await pageSizeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        var maxPages = Math.Max(1, hardCapBytes / Math.Max(1, pageSize));
        await ExecuteAsync(
            connection,
            null,
            $"PRAGMA max_page_count = {maxPages};",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var batch = new List<WriteRequest>(BatchSize);
                while (batch.Count < BatchSize && _queue.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                await WriteBatchAsync(batch).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            ReportFailure(DiagnosticFailureOperation.SessionWrite, DiagnosticFailureStage.WriteSession, exception);
            Volatile.Write(ref _degraded, 1);
            while (_queue.Reader.TryRead(out var item))
            {
                item.Completion?.TrySetResult(false);
            }
        }
    }

    private async Task WriteBatchAsync(IReadOnlyList<WriteRequest> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var path = batch[0].Path;
        try
        {
            await using (var connection = await OpenConnectionAsync(path, CancellationToken.None).ConfigureAwait(false))
            {
                await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false))
                {
                    foreach (var item in batch)
                    {
                        item.Apply(connection, transaction);
                    }

                    await transaction.CommitAsync().ConfigureAwait(false);
                    if (batch.Any(item => item.IsFlush))
                    {
                        await CheckpointAsync(connection, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }

            foreach (var item in batch)
            {
                item.Completion?.TrySetResult(true);
            }
            EnforcePhysicalCap(path);
        }
        catch (Exception exception)
        {
            ReportFailure(DiagnosticFailureOperation.SessionWrite, DiagnosticFailureStage.WriteSession, exception);
            Volatile.Write(ref _degraded, 1);
            lock (_gate)
            {
                if (_active?.Path.Equals(path, StringComparison.Ordinal) == true)
                {
                    _active.CaptureStopped = true;
                    _active.CaptureStoppedReason = "storage-failure";
                }
            }

            await PersistCaptureStoppedAsync(path, "storage-failure").ConfigureAwait(false);

            foreach (var item in batch)
            {
                item.Completion?.TrySetResult(false);
            }
        }
    }

    private async Task PersistCaptureStoppedAsync(string path, string reason)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(path, CancellationToken.None).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                transaction,
                "UPDATE Sessions SET CaptureStopped = 1, CaptureStoppedReason = $reason WHERE State = $state;",
                CancellationToken.None,
                ("$reason", reason),
                ("$state", ActiveState));
            await transaction.CommitAsync().ConfigureAwait(false);
            await CheckpointAsync(connection, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            Volatile.Write(ref _degraded, 1);
        }
    }

    private bool EnqueueRecord(
        ActiveSession active,
        long estimatedBytes,
        Action<SqliteConnection, SqliteTransaction> apply,
        bool force,
        TaskCompletionSource<bool>? completion = null,
        bool isCritical = false)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, active) ||
                !active.AcceptingWrites ||
                Volatile.Read(ref _disposed) != 0)
            {
                completion?.TrySetResult(false);
                return false;
            }

            if (!force && (active.CaptureStopped || !CanAccept(active, estimatedBytes)))
            {
                MarkCaptureStoppedLocked(active, "hard-cap");
                completion?.TrySetResult(false);
                return false;
            }

            active.RecordedBytes = Math.Min(long.MaxValue - estimatedBytes, active.RecordedBytes + estimatedBytes);
            var recordedBytes = active.RecordedBytes;
            var request = new WriteRequest(
                active.Path,
                (connection, transaction) =>
                {
                    apply(connection, transaction);
                    UpdateSessionMetadata(connection, transaction, active.SessionId, recordedBytes, active.CaptureStopped, active.CaptureStoppedReason);
                },
                completion,
                false);
            if (!TryWriteRequestLocked(active, request, isCritical))
            {
                active.RecordedBytes -= estimatedBytes;
                completion?.TrySetResult(false);
                return false;
            }

            return true;
        }
    }

    private void MarkCaptureStoppedLocked(ActiveSession active, string reason)
    {
        if (active.CaptureStopped || !active.AcceptingWrites)
        {
            return;
        }

        active.CaptureStopped = true;
        active.CaptureStoppedReason = reason;
        var definition = _registry.Get(new DiagnosticDefinitionId("diagnostics.capture_stopped"));
        var reasonField = definition.Fields.Single(field => field.Name == "reason");
        var fields = definition.CreateFields(DiagnosticFieldValue.Enum(reasonField, reason));
        var diagnosticEvent = new DiagnosticEvent(
            definition,
            fields,
            _contextAccessor.Current,
            _timeProvider.GetUtcNow());
        var payload = SerializeFields(fields);
        var estimate = EstimateBytes(definition.Id.Value, payload);
        var remainingBudget = active.HardCapBytes - ReserveBytes - estimate;
        if (remainingBudget <= 0 ||
            active.RecordedBytes > remainingBudget ||
            GetSessionStorageBytes(active.Path) > remainingBudget)
        {
            active.RecordedBytes = Math.Min(active.RecordedBytes, active.HardCapBytes);
            return;
        }

        var recordedBytes = Math.Min(active.HardCapBytes, active.RecordedBytes + estimate);
        active.RecordedBytes = recordedBytes;
        var request = new WriteRequest(
            active.Path,
            (connection, transaction) =>
            {
                InsertEvent(connection, transaction, diagnosticEvent, payload);
                UpdateSessionMetadata(connection, transaction, active.SessionId, recordedBytes, true, active.CaptureStoppedReason);
            },
            null,
            false);
        if (!TryWriteRequestLocked(active, request, isCritical: true))
        {
            active.RecordedBytes = Math.Max(0, active.RecordedBytes - estimate);
        }
    }

    private void EnqueueResourceSample(string trigger)
    {
        ActiveSession? active;
        lock (_gate)
        {
            active = _active;
        }

        if (active is null)
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            var heap = GC.GetTotalMemory(forceFullCollection: false);
            const long estimate = 192;
            lock (_gate)
            {
                if (!ReferenceEquals(_active, active) || !active.AcceptingWrites)
                {
                    return;
                }

                if (active.CaptureStopped || !CanAccept(active, estimate))
                {
                    MarkCaptureStoppedLocked(active, "hard-cap");
                    return;
                }

                active.RecordedBytes += estimate;
                var recordedBytes = active.RecordedBytes;
                var timestamp = _timeProvider.GetUtcNow().ToUniversalTime();
                if (!EnqueueResourceRequestLocked(active, timestamp, workingSet, heap, trigger, recordedBytes))
                {
                    active.RecordedBytes -= estimate;
                }
            }
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, active))
                {
                    active.CurrentProcessDroppedRecordCount++;
                }
            }
        }
    }

    private bool EnqueueResourceRequestLocked(
        ActiveSession active,
        DateTimeOffset timestamp,
        long workingSet,
        long heap,
        string trigger,
        long recordedBytes)
    {
        return TryWriteRequestLocked(
            active,
            new WriteRequest(
                active.Path,
                (connection, transaction) =>
                {
                    Execute(
                        connection,
                        transaction,
                        "INSERT INTO ResourceSamples (TimestampUtc, ProcessInstanceId, Trigger, CpuPercent, WorkingSetBytes, ManagedHeapBytes) VALUES ($timestampUtc, $processInstanceId, $trigger, 0, $workingSet, $heap);",
                        ("$timestampUtc", timestamp.ToString("O", CultureInfo.InvariantCulture)),
                        ("$processInstanceId", active.ProcessInstanceId),
                        ("$trigger", trigger),
                        ("$workingSet", workingSet),
                        ("$heap", heap));
                    UpdateSessionMetadata(connection, transaction, active.SessionId, recordedBytes, active.CaptureStopped, active.CaptureStoppedReason);
                },
                null,
                false),
            isCritical: false);
    }

    private bool TryWriteRequestLocked(ActiveSession active, WriteRequest request, bool isCritical)
    {
        if (!isCritical && _queue.Reader.Count >= _ordinaryQueueLimit)
        {
            active.CurrentProcessDroppedRecordCount++;
            return false;
        }

        if (_queue.Writer.TryWrite(request))
        {
            return true;
        }

        active.CurrentProcessDroppedRecordCount++;
        return false;
    }

    private static bool IsHighPriorityEvent(DiagnosticEvent diagnosticEvent) =>
        diagnosticEvent.Definition.Id.Value is
            "app.lifecycle" or
            "diagnostics.problem_marker" or
            "diagnostics.capture_stopped";

    private async Task FlushPathAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.Writer.WriteAsync(
            new WriteRequest(path, static (_, _) => { }, completion, true),
            cancellationToken).ConfigureAwait(false);
        _ = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(string path, CancellationToken cancellationToken)
    {
        path = _pathResolver.ResolvePath(path);

        SqliteRuntimeInitializer.EnsureInitialized();
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate;Cache=Private;Pooling=False")
        {
            DefaultTimeout = 5
        };
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                null,
                "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;",
                cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            "CREATE TABLE IF NOT EXISTS DiagnosticSchemaVersion (Version INTEGER NOT NULL PRIMARY KEY);",
            cancellationToken);
        var version = Convert.ToInt32(await ScalarAsync(
            connection,
            transaction,
            "SELECT COALESCE(MAX(Version), 0) FROM DiagnosticSchemaVersion;",
            cancellationToken));
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持的诊断 schema 版本：{version}。");
        }

        if (version == 0)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE Sessions (
                    SessionId TEXT NOT NULL PRIMARY KEY,
                    State TEXT NOT NULL,
                    StartedAtUtc TEXT NOT NULL,
                    EndedAtUtc TEXT NULL,
                    HardCapBytes INTEGER NOT NULL,
                    RecordedBytes INTEGER NOT NULL,
                    CaptureStopped INTEGER NOT NULL,
                    CaptureStoppedReason TEXT NULL,
                    EndedUnexpectedly INTEGER NOT NULL
                );
                CREATE TABLE Processes (
                    ProcessInstanceId TEXT NOT NULL PRIMARY KEY,
                    AppVersion TEXT NOT NULL,
                    StartedAtUtc TEXT NOT NULL,
                    EndedAtUtc TEXT NULL,
                    EndReason TEXT NULL
                );
                CREATE TABLE Activities (
                    ActivityId TEXT NOT NULL PRIMARY KEY,
                    OperationId TEXT NOT NULL,
                    ParentActivityId TEXT NULL,
                    ProcessInstanceId TEXT NOT NULL,
                    StartedAtUtc TEXT NOT NULL,
                    EndedAtUtc TEXT NULL,
                    Outcome TEXT NULL,
                    FailureCode TEXT NULL
                );
                CREATE TABLE Events (
                    RowId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc TEXT NOT NULL,
                    DefinitionId TEXT NOT NULL,
                    ProcessInstanceId TEXT NOT NULL,
                    ActivityId TEXT NULL,
                    FieldsJson TEXT NOT NULL
                );
                CREATE TABLE Snapshots (
                    RowId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc TEXT NOT NULL,
                    DefinitionId TEXT NOT NULL,
                    ProcessInstanceId TEXT NOT NULL,
                    ActivityId TEXT NULL,
                    FieldsJson TEXT NOT NULL
                );
                CREATE TABLE ResourceSamples (
                    RowId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc TEXT NOT NULL,
                    ProcessInstanceId TEXT NOT NULL,
                    Trigger TEXT NOT NULL,
                    CpuPercent REAL NOT NULL,
                    WorkingSetBytes INTEGER NOT NULL,
                    ManagedHeapBytes INTEGER NOT NULL
                );
                CREATE TABLE Environment (
                    Key TEXT NOT NULL PRIMARY KEY,
                    Value TEXT NOT NULL
                );
                CREATE TABLE Attachments (
                    AttachmentId TEXT NOT NULL PRIMARY KEY,
                    CapturedAtUtc TEXT NOT NULL,
                    ProcessInstanceId TEXT NOT NULL,
                    MimeType TEXT NOT NULL,
                    PixelWidth INTEGER NOT NULL,
                    PixelHeight INTEGER NOT NULL,
                    ByteLength INTEGER NOT NULL,
                    Payload BLOB NOT NULL
                );
                CREATE TABLE AnonymousObjectAssociations (
                    AssociationId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ObjectToken TEXT NOT NULL,
                    ObjectType TEXT NOT NULL,
                    ProcessInstanceId TEXT NOT NULL,
                    ActivityId TEXT NULL,
                    AssociatedAtUtc TEXT NOT NULL
                );
                INSERT INTO DiagnosticSchemaVersion (Version) VALUES (3);
                """,
                cancellationToken);
        }

        if (version == 1)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE AnonymousObjectAssociations (
                    AssociationId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ObjectToken TEXT NOT NULL,
                    ObjectType TEXT NOT NULL,
                    ProcessInstanceId TEXT NOT NULL,
                    ActivityId TEXT NULL,
                    AssociatedAtUtc TEXT NOT NULL
                );
                INSERT INTO DiagnosticSchemaVersion (Version) VALUES (2);
                """,
                cancellationToken);
            version = 2;
        }

        if (version == 2)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "ALTER TABLE Sessions ADD COLUMN CaptureStoppedReason TEXT NULL; INSERT INTO DiagnosticSchemaVersion (Version) VALUES (3);",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SessionRow?> ReadSessionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SessionId, State, StartedAtUtc, EndedAtUtc, HardCapBytes, RecordedBytes, CaptureStopped, CaptureStoppedReason, EndedUnexpectedly FROM Sessions LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SessionRow(
            reader.GetString(0),
            reader.GetString(1),
            ParseTimestamp(reader.GetString(2)),
            reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6) != 0,
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt64(8) != 0);
    }

    private static async Task<ProcessRow?> ReadLatestProcessAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ProcessInstanceId, EndedAtUtc FROM Processes ORDER BY StartedAtUtc DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ProcessRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : ParseTimestamp(reader.GetString(1)));
    }

    private async Task InsertProcessAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string processInstanceId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO Processes (ProcessInstanceId, AppVersion, StartedAtUtc, EndedAtUtc, EndReason) VALUES ($id, $appVersion, $startedAtUtc, NULL, NULL);",
            cancellationToken,
            ("$id", processInstanceId),
            ("$appVersion", ResolveAppVersion()),
            ("$startedAtUtc", startedAt.ToString("O", CultureInfo.InvariantCulture)));
    }

    private async Task InsertEnvironmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["osDescription"] = Environment.OSVersion.VersionString,
            ["frameworkDescription"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["processArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ["is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem.ToString(),
            ["processorCount"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["gcServer"] = System.Runtime.GCSettings.IsServerGC.ToString(),
            ["config.logLevel"] = _settings.Current.LogLevel,
            ["config.theme"] = _settings.Current.Theme,
            ["config.enablePerformanceTelemetry"] = _settings.Current.EnablePerformanceTelemetry.ToString(),
            ["config.defaultSpeakSpeed"] = _settings.Current.DefaultSpeakSpeed.ToString(CultureInfo.InvariantCulture),
            ["config.prefetchCount"] = _settings.Current.PrefetchCount.ToString(CultureInfo.InvariantCulture),
            ["config.readChapterTitle"] = _settings.Current.ReadChapterTitle.ToString()
        };
        foreach (var pair in values)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT OR REPLACE INTO Environment (Key, Value) VALUES ($key, $value);",
                cancellationToken,
                ("$key", pair.Key),
                ("$value", pair.Value));
        }
    }

    private static void InsertActivityStarted(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationStarted operation,
        DateTimeOffset timestamp)
    {
        Execute(
            connection,
            transaction,
            "INSERT OR REPLACE INTO Activities (ActivityId, OperationId, ParentActivityId, ProcessInstanceId, StartedAtUtc, EndedAtUtc, Outcome, FailureCode) VALUES ($activityId, $operationId, $parentActivityId, $processInstanceId, $startedAtUtc, NULL, NULL, NULL);",
            ("$activityId", operation.Context.ActivityId!),
            ("$operationId", operation.Operation.Id.Value),
            ("$parentActivityId", operation.ParentActivityId),
            ("$processInstanceId", operation.Context.ProcessInstanceId),
            ("$startedAtUtc", timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
    }

    private static void UpdateActivityCompleted(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationCompleted operation)
    {
        Execute(
            connection,
            transaction,
            "UPDATE Activities SET EndedAtUtc = $endedAtUtc, Outcome = $outcome, FailureCode = $failureCode WHERE ActivityId = $activityId;",
            ("$endedAtUtc", operation.CompletedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("$outcome", operation.Result.Outcome.ToString().ToLowerInvariant()),
            ("$failureCode", operation.Result.FailureCode),
            ("$activityId", operation.Context.ActivityId!));
    }

    private static void InsertEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DiagnosticEvent diagnosticEvent,
        string payload)
    {
        Execute(
            connection,
            transaction,
            "INSERT INTO Events (TimestampUtc, DefinitionId, ProcessInstanceId, ActivityId, FieldsJson) VALUES ($timestampUtc, $definitionId, $processInstanceId, $activityId, $fieldsJson);",
            ("$timestampUtc", diagnosticEvent.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("$definitionId", diagnosticEvent.Definition.Id.Value),
            ("$processInstanceId", diagnosticEvent.Context.ProcessInstanceId),
            ("$activityId", diagnosticEvent.Context.ActivityId),
            ("$fieldsJson", payload));
    }

    private static void InsertSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DiagnosticEvent diagnosticEvent,
        string payload)
    {
        Execute(
            connection,
            transaction,
            "INSERT INTO Snapshots (TimestampUtc, DefinitionId, ProcessInstanceId, ActivityId, FieldsJson) VALUES ($timestampUtc, $definitionId, $processInstanceId, $activityId, $fieldsJson);",
            ("$timestampUtc", diagnosticEvent.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("$definitionId", diagnosticEvent.Definition.Id.Value),
            ("$processInstanceId", diagnosticEvent.Context.ProcessInstanceId),
            ("$activityId", diagnosticEvent.Context.ActivityId),
            ("$fieldsJson", payload));
    }

    private static void InsertAttachment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DiagnosticAttachment attachment,
        byte[] content,
        string processInstanceId)
    {
        Execute(
            connection,
            transaction,
            "INSERT INTO Attachments (AttachmentId, CapturedAtUtc, ProcessInstanceId, MimeType, PixelWidth, PixelHeight, ByteLength, Payload) VALUES ($attachmentId, $capturedAtUtc, $processInstanceId, $mimeType, $pixelWidth, $pixelHeight, $byteLength, $payload);",
            ("$attachmentId", attachment.AttachmentId),
            ("$capturedAtUtc", attachment.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("$processInstanceId", processInstanceId),
            ("$mimeType", attachment.MimeType),
            ("$pixelWidth", attachment.PixelWidth),
            ("$pixelHeight", attachment.PixelHeight),
            ("$byteLength", content.LongLength),
            ("$payload", content));
    }

    private static void InsertAnonymousObjectAssociation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string objectToken,
        string objectType,
        string processInstanceId,
        string? activityId,
        DateTimeOffset associatedAtUtc)
    {
        Execute(
            connection,
            transaction,
            "INSERT INTO AnonymousObjectAssociations (ObjectToken, ObjectType, ProcessInstanceId, ActivityId, AssociatedAtUtc) VALUES ($objectToken, $objectType, $processInstanceId, $activityId, $associatedAtUtc);",
            ("$objectToken", objectToken),
            ("$objectType", objectType),
            ("$processInstanceId", processInstanceId),
            ("$activityId", activityId),
            ("$associatedAtUtc", associatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
    }

    private static void UpdateSessionMetadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionId,
        long recordedBytes,
        bool captureStopped,
        string? captureStoppedReason)
    {
        Execute(
            connection,
            transaction,
            "UPDATE Sessions SET RecordedBytes = $recordedBytes, CaptureStopped = $captureStopped, CaptureStoppedReason = $captureStoppedReason WHERE SessionId = $sessionId;",
            ("$recordedBytes", recordedBytes),
            ("$captureStopped", captureStopped ? 1 : 0),
            ("$captureStoppedReason", captureStoppedReason),
            ("$sessionId", sessionId));
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        command.ExecuteNonQuery();
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
    }

    private static async Task CheckpointAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await using var reader = await checkpoint
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ||
                reader.GetInt32(0) != 0)
            {
                throw new IOException("SQLite WAL checkpoint 未完成。");
            }
        }

        string? journalMode;
        await using (var journal = connection.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode = DELETE;";
            journalMode = Convert.ToString(
                await journal.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("SQLite journal 未切换为自包含模式。");
        }

        var dataSource = connection.DataSource;
        if (File.Exists(dataSource + "-wal") || File.Exists(dataSource + "-shm"))
        {
            throw new IOException("SQLite WAL sidecar 仍存在。");
        }

    }

    private static string SerializeFields(DiagnosticFieldSet fields)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var value in fields.Values)
        {
            values[value.Field.Name] = value.Type switch
            {
                DiagnosticFieldType.Boolean => value.BooleanValue,
                DiagnosticFieldType.Integer => value.IntegerValue,
                DiagnosticFieldType.Decimal or DiagnosticFieldType.DurationMilliseconds => value.DecimalValue,
                DiagnosticFieldType.Enum or DiagnosticFieldType.String => value.StringValue,
                _ => null
            };
        }

        return JsonSerializer.Serialize(values, JsonOptions);
    }

    private bool IsRegisteredDefinition(DiagnosticDefinition definition, DiagnosticFieldSet fields)
    {
        try
        {
            return ReferenceEquals(_registry.Get(definition.Id), definition) && ReferenceEquals(fields.Definition, definition);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool IsSessionContext(ActiveSession active, CorrelationContext context) =>
        string.Equals(context.DiagnosticSessionId, active.SessionId, StringComparison.Ordinal);

    private static long EstimateBytes(string first, string second) =>
        Math.Max(128, Encoding.UTF8.GetByteCount(first) + Encoding.UTF8.GetByteCount(second) + 192);

    private bool CanAccept(ActiveSession active, long estimatedBytes)
    {
        var remainingBudget = active.HardCapBytes - ReserveBytes - estimatedBytes;
        if (remainingBudget <= 0 || active.RecordedBytes > remainingBudget)
        {
            return false;
        }

        // Include the SQLite file and WAL sidecars so queued estimates cannot hide an already-full file.
        return GetSessionStorageBytes(active.Path) <= remainingBudget;
    }

    private void EnforcePhysicalCap(string path)
    {
        try
        {
            var bytes = GetSessionStorageBytes(path);
            lock (_gate)
            {
                if (_active?.Path.Equals(path, StringComparison.Ordinal) == true &&
                    _active.AcceptingWrites &&
                    bytes >= _active.HardCapBytes)
                {
                    MarkCaptureStoppedLocked(_active, "hard-cap");
                }
            }
        }
        catch
        {
            Volatile.Write(ref _degraded, 1);
        }
    }

    private long GetSessionStorageBytes(string path)
    {
        var total = TryGetFileLength(path);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            total += TryGetFileLength(path + suffix);
        }

        return total;
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
    }

    private string? ReadMarkerFileName()
    {
        var markerPath = _pathResolver.ResolvePath(_directories.ActiveDiagnosticSessionMarkerPath);
        if (!File.Exists(markerPath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(markerPath);
        if (bytes.Length == 0 || bytes.Length > MarkerMaxBytes)
        {
            throw new InvalidDataException("诊断活动 marker 无效。");
        }

        var value = Encoding.UTF8.GetString(bytes).Trim('\uFEFF', ' ', '\r', '\n', '\t');
        return ResolveDiagnosticFile(value) is null ? throw new InvalidDataException("诊断活动 marker 路径无效。") : value;
    }

    private string? ResolveDiagnosticFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            Path.IsPathRooted(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !fileName.StartsWith("session-", StringComparison.Ordinal) ||
            !fileName.EndsWith(".nsdiag", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return _pathResolver.ResolvePath(Path.Combine(_directories.DiagnosticsDirectoryPath, fileName));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private async Task WriteMarkerAsync(string fileName, CancellationToken cancellationToken)
    {
        var marker = _pathResolver.ResolvePath(_directories.ActiveDiagnosticSessionMarkerPath);
        var temporary = _pathResolver.ResolvePath(marker + ".tmp");
        await File.WriteAllTextAsync(temporary, fileName, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, marker, true);
    }

    private void TryDeleteMarker(string sessionPath)
    {
        try
        {
            var markerFileName = ReadMarkerFileName();
            if (markerFileName is not null &&
                string.Equals(ResolveDiagnosticFile(markerFileName), Path.GetFullPath(sessionPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                File.Delete(_pathResolver.ResolvePath(_directories.ActiveDiagnosticSessionMarkerPath));
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _degraded, 1);
            ReportFailure(DiagnosticFailureOperation.SessionEnd, DiagnosticFailureStage.EndSession, exception);
        }
    }

    private void ReportFailure(
        DiagnosticFailureOperation operation,
        DiagnosticFailureStage stage,
        Exception exception) =>
        _failureReporter?.ReportFailure(operation, stage, exception);

    private void TryDeleteTrustedFile(string path)
    {
        try
        {
            var trustedPath = _pathResolver.ResolvePath(path);
            if (File.Exists(trustedPath))
            {
                File.Delete(trustedPath);
            }
        }
        catch
        {
        }
    }

    private DiagnosticSessionSnapshot? SnapshotActive()
    {
        lock (_gate)
        {
            return _active?.ToSnapshot();
        }
    }

    private static DiagnosticSessionSnapshot ToSnapshot(SessionRow session, string processInstanceId) =>
        new(
            session.SessionId,
            string.Equals(session.State, ActiveState, StringComparison.Ordinal)
                ? DiagnosticSessionState.Active
                : DiagnosticSessionState.Ended,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.HardCapBytes,
            session.RecordedBytes,
            session.CaptureStopped,
            session.EndedUnexpectedly,
            processInstanceId,
            session.CaptureStoppedReason);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static string CreateToken() => Guid.NewGuid().ToString("N");

    private static string CreateAnonymousToken(
        string sessionId,
        string objectType,
        string objectIdentity)
    {
        var input = Encoding.UTF8.GetBytes(sessionId + "\u001f" + objectType + "\u001f" + objectIdentity);
        var digest = SHA256.HashData(input);
        return $"{objectType}-{Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static string ResolveAppVersion() =>
        typeof(SqliteDiagnosticSessionStore).Assembly.GetName().Version?.ToString() ?? "unknown";

    private sealed class ActiveSession
    {
        public ActiveSession(
            string sessionId,
            string path,
            DateTimeOffset startedAtUtc,
            long hardCapBytes,
            string processInstanceId,
            long recordedBytes,
            bool captureStopped,
            bool endedUnexpectedly,
            string? captureStoppedReason = null)
        {
            SessionId = sessionId;
            Path = path;
            StartedAtUtc = startedAtUtc;
            HardCapBytes = hardCapBytes;
            ProcessInstanceId = processInstanceId;
            RecordedBytes = recordedBytes;
            CaptureStopped = captureStopped;
            CaptureStoppedReason = captureStoppedReason;
            EndedUnexpectedly = endedUnexpectedly;
        }

        public string SessionId { get; }
        public string Path { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public long HardCapBytes { get; }
        public string ProcessInstanceId { get; }
        public long RecordedBytes { get; set; }
        public long CurrentProcessDroppedRecordCount { get; set; }
        public bool CaptureStopped { get; set; }
        public string? CaptureStoppedReason { get; set; }
        public bool AcceptingWrites { get; set; } = true;
        public bool EndedUnexpectedly { get; }
        public HashSet<string> ActiveActivityIds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> AnonymousObjectTokens { get; } = new(StringComparer.Ordinal);

        public DiagnosticSessionSnapshot ToSnapshot(
            DateTimeOffset? endedAtUtc = null,
            DiagnosticSessionState state = DiagnosticSessionState.Active) =>
            new(
                SessionId,
                state,
                StartedAtUtc,
                endedAtUtc,
                HardCapBytes,
                RecordedBytes,
                CaptureStopped,
                EndedUnexpectedly,
                ProcessInstanceId,
                CaptureStoppedReason,
                CurrentProcessDroppedRecordCount);
    }

    private sealed record SessionRow(
        string SessionId,
        string State,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        long HardCapBytes,
        long RecordedBytes,
        bool CaptureStopped,
        string? CaptureStoppedReason,
        bool EndedUnexpectedly);

    private sealed record ProcessRow(string ProcessInstanceId, DateTimeOffset? EndedAtUtc);

    private sealed record WriteRequest(
        string Path,
        Action<SqliteConnection, SqliteTransaction> Apply,
        TaskCompletionSource<bool>? Completion,
        bool IsFlush);
}
