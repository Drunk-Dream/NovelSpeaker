using System.Text;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Diagnostics;

public sealed class SqliteDiagnosticSessionStoreTests
{
    [DirectoryLinkFact]
    public async Task Start_and_recover_allow_reparse_points_above_the_data_root()
    {
        var installationRoot = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Scoop-" + Path.GetRandomFileName());
        var versionDirectory = Path.Combine(installationRoot, "1.0.0");
        var currentDirectory = Path.Combine(installationRoot, "current");
        Directory.CreateDirectory(versionDirectory);
        DirectoryLinkTestHelper.CreateDirectoryLink(currentDirectory, versionDirectory);

        var fixture = new Fixture("process-one", Path.Combine(currentDirectory, "Data"));
        var first = fixture.CreateStore();
        var started = await first.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        await first.NotifyProcessShutdownAsync(CancellationToken.None);
        await first.DisposeAsync();

        await using var second = fixture.CreateStore("process-two");
        var recovered = await second.RecoverAsync(CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(started.SessionId, recovered!.SessionId);
    }

    [DirectoryLinkFact]
    public async Task Start_and_recover_allow_the_data_root_itself_to_be_a_symbolic_link()
    {
        var installationRoot = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Scoop-" + Path.GetRandomFileName());
        var logicalDataRoot = Path.Combine(installationRoot, "current", "Data");
        var persistedDataRoot = Path.Combine(installationRoot, "persist", "novelspeaker", "Data");
        Directory.CreateDirectory(persistedDataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(logicalDataRoot)!);
        DirectoryLinkTestHelper.CreateDirectoryLink(logicalDataRoot, persistedDataRoot);

        var fixture = new Fixture("process-one", logicalDataRoot);
        await using (var database = await new SqliteConnectionFactory(fixture.Directories)
                         .OpenConnectionAsync(CancellationToken.None))
        {
            Assert.Equal(System.Data.ConnectionState.Open, database.State);
        }

        var first = fixture.CreateStore();
        var started = await first.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        await first.NotifyProcessShutdownAsync(CancellationToken.None);
        await first.DisposeAsync();

        await using var second = fixture.CreateStore("process-two");
        var recovered = await second.RecoverAsync(CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(started.SessionId, recovered!.SessionId);
    }

    [Fact]
    public async Task Start_is_the_only_operation_that_creates_a_session_file()
    {
        var fixture = new Fixture("process-one");
        await using var store = fixture.CreateStore();

        Assert.Empty(Directory.EnumerateFiles(fixture.Directories.DiagnosticsDirectoryPath, "*.nsdiag"));
        Assert.Null(store.Current);

        var started = await store.StartAsync(new DiagnosticSessionStartOptions(1 * 1024 * 1024), CancellationToken.None);

        var files = Directory.EnumerateFiles(fixture.Directories.DiagnosticsDirectoryPath, "*.nsdiag").ToArray();
        Assert.Single(files);
        Assert.Equal(DiagnosticSessionState.Active, started.State);
        Assert.True(File.Exists(fixture.Directories.ActiveDiagnosticSessionMarkerPath));
    }

    [Fact]
    public async Task End_commits_ended_state_before_removing_the_active_marker()
    {
        var fixture = new Fixture("process-one");
        await using var store = fixture.CreateStore();
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);

        var ended = await store.EndAsync(CancellationToken.None);
        var sessionPath = fixture.SessionPath(started.SessionId);

        Assert.Equal(DiagnosticSessionState.Ended, ended.State);
        Assert.Null(store.Current);
        Assert.Equal(ended, store.LastEnded);
        Assert.False(File.Exists(fixture.Directories.ActiveDiagnosticSessionMarkerPath));
        Assert.Equal("ended", await fixture.ScalarTextAsync(sessionPath, "SELECT State FROM Sessions;"));
        Assert.False(File.Exists(sessionPath + "-wal"));
    }

    [Fact]
    public async Task Normal_process_exit_allows_the_same_active_session_to_resume()
    {
        var fixture = new Fixture("process-one");
        var first = fixture.CreateStore();
        var started = await first.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        await first.NotifyProcessShutdownAsync(CancellationToken.None);
        await first.DisposeAsync();

        var second = fixture.CreateStore("process-two");
        await using (second)
        {
            var recovered = await second.RecoverAsync(CancellationToken.None);

            Assert.NotNull(recovered);
            Assert.Equal(started.SessionId, recovered!.SessionId);
            Assert.False(recovered.EndedUnexpectedly);
            Assert.Equal("normal-exit", await fixture.ScalarTextAsync(
                fixture.SessionPath(started.SessionId),
                "SELECT EndReason FROM Processes WHERE ProcessInstanceId = 'process-one';"));
            Assert.Equal("process-two", recovered.CurrentProcessInstanceId);
        }
    }

    [Fact]
    public async Task Missing_process_end_is_recorded_as_unexpected_without_claiming_crash()
    {
        var fixture = new Fixture("process-one");
        var first = fixture.CreateStore();
        var started = await first.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);

        var second = fixture.CreateStore("process-two");
        await using (second)
        {
            var recovered = await second.RecoverAsync(CancellationToken.None);

            Assert.NotNull(recovered);
            Assert.True(recovered!.EndedUnexpectedly);
            Assert.Equal("unexpected", await fixture.ScalarTextAsync(
                fixture.SessionPath(started.SessionId),
                "SELECT EndReason FROM Processes WHERE ProcessInstanceId = 'process-one';"));
        }

        await first.DisposeAsync();
    }

    [Fact]
    public async Task Corrupt_marker_is_ignored_and_removed_during_recovery()
    {
        var fixture = new Fixture("process-one");
        await fixture.Directories.EnsureCreatedAsync(CancellationToken.None);
        await File.WriteAllTextAsync(
            fixture.Directories.ActiveDiagnosticSessionMarkerPath,
            "../not-a-session.nsdiag");
        await using var store = fixture.CreateStore();

        var recovered = await store.RecoverAsync(CancellationToken.None);

        Assert.Null(recovered);
        Assert.False(File.Exists(fixture.Directories.ActiveDiagnosticSessionMarkerPath));
    }

    [Fact]
    public async Task Registry_events_activities_snapshots_and_marker_resource_sample_are_persisted()
    {
        var fixture = new Fixture("process-one");
        await using var store = fixture.CreateStore();
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        var hub = new ObservabilityHub(fixture.Context, [store], fixture.Clock);

        using (var operation = hub.StartOperation(OperationCatalog.PlaybackStart))
        {
            operation.Complete(OperationResult.Succeeded());
        }

        Assert.True(await store.RecordProblemMarkerAsync(CancellationToken.None));
        await store.FlushAsync(CancellationToken.None);
        await store.EndAsync(CancellationToken.None);

        var sessionPath = fixture.SessionPath(started.SessionId);
        Assert.Equal(1L, await fixture.ScalarAsync(sessionPath, "SELECT COUNT(*) FROM Activities;"));
        Assert.True(await fixture.ScalarAsync(sessionPath, "SELECT COUNT(*) FROM Events;") >= 1L);
        Assert.Equal(1L, await fixture.ScalarAsync(sessionPath, "SELECT COUNT(*) FROM Snapshots;"));
        Assert.Equal(1L, await fixture.ScalarAsync(sessionPath, "SELECT COUNT(*) FROM ResourceSamples;"));
    }

    [Fact]
    public async Task Anonymous_association_is_stable_inside_a_session_and_is_not_persisted()
    {
        var fixture = new Fixture("process-one");
        await using var store = fixture.CreateStore();
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);

        var first = store.GetOrCreateAnonymousObjectToken("book", "private-book-id");
        var second = store.GetOrCreateAnonymousObjectToken("book", "private-book-id");
        var other = store.GetOrCreateAnonymousObjectToken("book", "another-private-book-id");
        await store.EndAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.Equal(2L, await fixture.ScalarAsync(
            fixture.SessionPath(started.SessionId),
            "SELECT COUNT(*) FROM AnonymousObjectAssociations;"));
        await store.DisposeAsync();
        var databaseBytes = fixture.ReadBytes(fixture.SessionPath(started.SessionId));
        Assert.DoesNotContain("private-book-id", Encoding.UTF8.GetString(databaseBytes));
    }

    [Fact]
    public async Task Late_notifications_from_an_ended_session_do_not_enter_the_next_session()
    {
        var fixture = new Fixture("process-one");
        await using var store = fixture.CreateStore();
        var hub = new ObservabilityHub(fixture.Context, [store], fixture.Clock);
        var first = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        using var operation = hub.StartOperation(OperationCatalog.PlaybackStart);
        var oldContext = operation.Context;

        await store.EndAsync(CancellationToken.None);
        var second = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);

        operation.Complete(OperationResult.Succeeded());
        var definition = DiagnosticRegistry.Default.Get(new DiagnosticDefinitionId("app.lifecycle"));
        var phase = definition.Fields.Single(field => field.Name == "phase");
        store.OnDiagnosticEvent(new DiagnosticEvent(
            definition,
            definition.CreateFields(DiagnosticFieldValue.Enum(phase, "startup")),
            oldContext,
            fixture.Clock.GetUtcNow()));
        await store.FlushAsync(CancellationToken.None);

        Assert.Equal(0L, await fixture.ScalarAsync(
            fixture.SessionPath(second.SessionId),
            "SELECT COUNT(*) FROM Events;"));
    }

    [Fact]
    public async Task Hard_cap_stops_collection_but_leaves_a_readable_ended_database()
    {
        var fixture = new Fixture("process-one");
        await using var store = fixture.CreateStore();
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(1 * 1024 * 1024), CancellationToken.None);

        for (var index = 0; index < 2000; index++)
        {
            await store.RecordProblemMarkerAsync(CancellationToken.None);
        }

        await store.FlushAsync(CancellationToken.None);
        Assert.True(store.Current!.CaptureStopped);
        var ended = await store.EndAsync(CancellationToken.None);

        Assert.Equal(DiagnosticSessionState.Ended, ended.State);
        Assert.True(await fixture.ScalarAsync(
            fixture.SessionPath(started.SessionId),
            "SELECT COUNT(*) FROM Events;") > 0L);
        Assert.Equal("ended", await fixture.ScalarTextAsync(
            fixture.SessionPath(started.SessionId),
            "SELECT State FROM Sessions;"));
    }

    [Fact]
    public async Task Diagnostic_writer_failure_does_not_change_a_business_operation_result()
    {
        var fixture = new Fixture("process-one");
        await using var store = fixture.CreateStore();
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        var sessionPath = fixture.SessionPath(started.SessionId);
        var before = fixture.ReadBytes(sessionPath);
        using var blocker = new FileStream(sessionPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var context = fixture.Context;
        var recordingConsumer = new RecordingConsumer();
        var hub = new ObservabilityHub(context, [store, recordingConsumer], fixture.Clock);
        using var operation = hub.StartOperation(OperationCatalog.PlaybackStart);

        operation.Complete(OperationResult.Succeeded());
        Assert.True(SpinWait.SpinUntil(() => store.IsDegraded, TimeSpan.FromSeconds(10)));

        Assert.True(operation.IsCompleted);
        Assert.True(store.IsDegraded);
        Assert.NotNull(recordingConsumer.Completed);
        Assert.Equal(OperationOutcome.Succeeded, recordingConsumer.Completed!.Result.Outcome);
        blocker.Dispose();
        Assert.Equal(before, fixture.ReadBytes(sessionPath));
    }

    [Fact]
    public async Task Marker_writer_failure_returns_not_recorded_without_throwing()
    {
        var fixture = new Fixture("process-one");
        await using var store = fixture.CreateStore();
        var started = await store.StartAsync(new DiagnosticSessionStartOptions(), CancellationToken.None);
        using var blocker = new FileStream(
            fixture.SessionPath(started.SessionId),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var recorded = await store.RecordProblemMarkerAsync(CancellationToken.None);

        Assert.False(recorded);
        Assert.True(store.IsDegraded);
    }

    private sealed class RecordingConsumer : IObservabilityConsumer
    {
        public OperationCompleted? Completed { get; private set; }

        public void OnOperationStarted(OperationStarted operation)
        {
        }

        public void OnOperationCompleted(OperationCompleted operation) => Completed = operation;

        public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent)
        {
        }
    }

    private sealed class Fixture
    {
        public Fixture(string processInstanceId, string? root = null)
        {
            Root = root ?? Path.Combine(Path.GetTempPath(), "NovelSpeaker-Diagnostic-" + Path.GetRandomFileName());
            Directories = new AppDataDirectoryProvider(Root);
            Directories.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
            Clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Context = new ObservabilityContextAccessor(processInstanceId);
        }

        public string Root { get; }
        public AppDataDirectoryProvider Directories { get; }
        public ManualTimeProvider Clock { get; }
        public ObservabilityContextAccessor Context { get; }

        public SqliteDiagnosticSessionStore CreateStore(string? processInstanceId = null)
        {
            var context = processInstanceId is null ? Context : new ObservabilityContextAccessor(processInstanceId);
            return new SqliteDiagnosticSessionStore(
                Directories,
                context,
                Clock,
                new TestAppSettingsService(AppSettings.Default),
                new AppStoragePathResolver(Directories));
        }

        public string SessionPath(string sessionId) =>
            Path.Combine(Directories.DiagnosticsDirectoryPath, $"session-{sessionId}.nsdiag");

        public async Task<long> ScalarAsync(string path, string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async Task<string?> ScalarTextAsync(string path, string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(await command.ExecuteScalarAsync());
        }

        public byte[] ReadBytes(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
