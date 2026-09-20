using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Presentation.Platform;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class DiagnosticToolViewModelTests
{
    [Fact]
    public async Task Opening_the_tool_does_not_start_a_session_until_start_is_clicked()
    {
        var sessions = new FakeSessionService();
        var viewModel = CreateViewModel(sessions);

        Assert.Equal(DiagnosticToolState.Preparing, viewModel.State);
        Assert.Null(sessions.Current);

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal(DiagnosticToolState.Capturing, viewModel.State);
        Assert.Equal(1, sessions.StartCount);
    }

    [Fact]
    public async Task Marker_and_explicit_window_capture_are_optional_and_repeatable()
    {
        var sessions = new FakeSessionService();
        var viewModel = CreateViewModel(sessions);
        await viewModel.StartCommand.ExecuteAsync(null);

        await viewModel.MarkProblemCommand.ExecuteAsync(null);
        await viewModel.MarkProblemCommand.ExecuteAsync(null);
        await viewModel.CaptureWindowCommand.ExecuteAsync(null);

        Assert.Equal(2, sessions.MarkerCount);
        Assert.Equal(1, sessions.AttachmentCount);
        Assert.Equal("0 MB / 64 MB", viewModel.SessionStorageText);
    }

    [Fact]
    public async Task End_then_restart_ends_the_old_session_and_starts_a_new_one()
    {
        var sessions = new FakeSessionService();
        var viewModel = CreateViewModel(sessions);
        await viewModel.StartCommand.ExecuteAsync(null);
        var firstSessionId = sessions.Current!.SessionId;

        await viewModel.EndCommand.ExecuteAsync(null);
        await viewModel.RestartCommand.ExecuteAsync(null);

        Assert.Equal(2, sessions.StartCount);
        Assert.Equal(1, sessions.EndCount);
        Assert.NotEqual(firstSessionId, sessions.Current!.SessionId);
        Assert.Equal(DiagnosticToolState.Capturing, viewModel.State);
    }

    [Fact]
    public async Task Completed_session_can_be_exported_without_reopening_it()
    {
        var sessions = new FakeSessionService();
        var exports = new FakeExportService { DestinationPath = "problem.zip" };
        var viewModel = CreateViewModel(sessions, exports);
        await viewModel.StartCommand.ExecuteAsync(null);
        await viewModel.EndCommand.ExecuteAsync(null);

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Equal("problem.zip", exports.LastDestinationPath);
        Assert.Equal(DiagnosticToolState.Completed, viewModel.State);
    }

    [Fact]
    public async Task Problem_export_suggests_filename_using_local_time()
    {
        var sessions = new FakeSessionService();
        var fileDialogs = new FakeFileDialogs { SavePath = "problem.zip" };
        var viewModel = CreateViewModel(sessions, fileDialogs: fileDialogs);
        await viewModel.StartCommand.ExecuteAsync(null);
        await viewModel.EndCommand.ExecuteAsync(null);

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Equal("NovelSpeaker-Problem-Diagnostics-20260101-080000.zip", fileDialogs.SuggestedFileName);
    }

    [Fact]
    public async Task Hard_cap_refresh_disables_optional_capture_actions_but_keeps_end_available()
    {
        var sessions = new FakeSessionService();
        var viewModel = CreateViewModel(sessions);
        await viewModel.StartCommand.ExecuteAsync(null);

        sessions.StopCapture();
        viewModel.RefreshDuration();

        Assert.True(viewModel.IsCaptureStopped);
        Assert.Contains("容量上限", viewModel.StateText, StringComparison.Ordinal);
        Assert.False(viewModel.MarkProblemCommand.CanExecute(null));
        Assert.False(viewModel.CaptureWindowCommand.CanExecute(null));
        Assert.True(viewModel.EndCommand.CanExecute(null));
    }

    [Fact]
    public async Task Window_capture_failure_is_reported_without_ending_the_session()
    {
        var sessions = new FakeSessionService();
        var viewModel = CreateViewModel(sessions, windowCapture: new FaultingWindowCapture());
        await viewModel.StartCommand.ExecuteAsync(null);

        await viewModel.CaptureWindowCommand.ExecuteAsync(null);

        Assert.Equal(DiagnosticToolState.Capturing, viewModel.State);
        Assert.NotEmpty(viewModel.ErrorText);
        Assert.NotNull(sessions.Current);
    }

    [Fact]
    public async Task Marker_failure_is_reported_without_incrementing_the_displayed_count()
    {
        var sessions = new FakeSessionService { MarkerResult = false };
        var viewModel = CreateViewModel(sessions);
        await viewModel.StartCommand.ExecuteAsync(null);

        await viewModel.MarkProblemCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.ErrorText);
        Assert.Equal(DiagnosticToolState.Capturing, viewModel.State);
    }

    [Fact]
    public async Task Recovery_projects_the_persisted_capacity_stopped_reason_and_recorded_bytes()
    {
        var sessions = new FakeSessionService();
        sessions.Restore(new DiagnosticSessionSnapshot(
            "recovered",
            DiagnosticSessionState.Active,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            DiagnosticSessionCapacityPresets.LargeBytes,
            2 * 1024 * 1024,
            true,
            true,
            "process",
            "storage-failure",
            12));
        var recording = new DiagnosticRecordingController(sessions);

        await recording.RecoverAsync(CancellationToken.None);
        var viewModel = CreateViewModel(sessions, recording: recording);

        Assert.Equal(DiagnosticToolState.Capturing, viewModel.State);
        Assert.Equal("2 MB / 256 MB", viewModel.SessionStorageText);
        Assert.Equal("本进程队列已跳过 12 条记录", viewModel.DroppedRecordText);
        Assert.Contains("存储", viewModel.StateText, StringComparison.Ordinal);
        Assert.True(viewModel.IsCaptureStopped);
        Assert.False(viewModel.CaptureWindowCommand.CanExecute(null));
        Assert.False(viewModel.MarkProblemCommand.CanExecute(null));
        Assert.True(viewModel.EndCommand.CanExecute(null));
    }

    [Fact]
    public void Ended_session_snapshot_projects_completed_state_after_recovery()
    {
        var sessions = new FakeSessionService();
        sessions.RestoreEnded(new DiagnosticSessionSnapshot(
            "ended",
            DiagnosticSessionState.Ended,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero),
            DiagnosticSessionCapacityPresets.SmallBytes,
            512 * 1024,
            false,
            false,
            "process"));
        var viewModel = CreateViewModel(sessions);

        Assert.Equal(DiagnosticToolState.Completed, viewModel.State);
        Assert.Equal("0.5 MB / 16 MB", viewModel.SessionStorageText);
        Assert.True(viewModel.RestartCommand.CanExecute(null));
        Assert.True(viewModel.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Marker_command_contains_service_exceptions_inside_the_shared_error_boundary()
    {
        var sessions = new FakeSessionService { ThrowOnMarker = true };
        var viewModel = CreateViewModel(sessions);
        await viewModel.StartCommand.ExecuteAsync(null);

        await viewModel.MarkProblemCommand.ExecuteAsync(null);

        Assert.Equal("无法记录问题标记。", viewModel.ErrorText);
        Assert.Equal(DiagnosticToolState.Capturing, viewModel.State);
    }

    private static DiagnosticToolViewModel CreateViewModel(
        FakeSessionService sessions,
        FakeExportService? exports = null,
        IDiagnosticWindowCapture? windowCapture = null,
        DiagnosticRecordingController? recording = null,
        FakeFileDialogs? fileDialogs = null) =>
        new(
            recording ?? new DiagnosticRecordingController(sessions),
            exports ?? new FakeExportService { DestinationPath = "problem.zip" },
            windowCapture ?? new FakeWindowCapture(),
            fileDialogs ?? new FakeFileDialogs { SavePath = "problem.zip" },
            new FixedTimeProvider());

    private sealed class FakeSessionService : IDiagnosticSessionService
    {
        private int _sessionNumber;

        public DiagnosticSessionSnapshot? Current { get; private set; }
        public DiagnosticSessionSnapshot? LastEnded { get; private set; }
        public int StartCount { get; private set; }
        public int EndCount { get; private set; }
        public int MarkerCount { get; private set; }
        public int AttachmentCount { get; private set; }
        public bool MarkerResult { get; init; } = true;
        public bool ThrowOnMarker { get; init; }

        public void Restore(DiagnosticSessionSnapshot snapshot) => Current = snapshot;

        public void RestoreEnded(DiagnosticSessionSnapshot snapshot) => LastEnded = snapshot;

        public void StopCapture()
        {
            if (Current is not { } current)
            {
                return;
            }

            Current = current with
            {
                CaptureStopped = true,
                CaptureStoppedReason = "hard-cap"
            };
        }

        public Task<DiagnosticSessionSnapshot> StartAsync(
            DiagnosticSessionStartOptions options,
            CancellationToken cancellationToken)
        {
            StartCount++;
            var id = "session-" + ++_sessionNumber;
            Current = new DiagnosticSessionSnapshot(
                id,
                DiagnosticSessionState.Active,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                null,
                options.HardCapBytes,
                0,
                false,
                false,
                "process");
            return Task.FromResult(Current);
        }

        public Task<DiagnosticSessionSnapshot?> RecoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Current ?? LastEnded);

        public Task<DiagnosticSessionSnapshot> EndAsync(CancellationToken cancellationToken)
        {
            EndCount++;
            var current = Current ?? throw new InvalidOperationException();
            LastEnded = current with
            {
                State = DiagnosticSessionState.Ended,
                EndedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero)
            };
            Current = null;
            return Task.FromResult(LastEnded);
        }

        public Task NotifyProcessShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddAttachmentAsync(DiagnosticAttachment attachment, CancellationToken cancellationToken)
        {
            AttachmentCount++;
            return Task.CompletedTask;
        }

        public Task<bool> RecordProblemMarkerAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnMarker)
            {
                throw new IOException("sensitive-path-marker-error");
            }

            if (!MarkerResult)
            {
                return Task.FromResult(false);
            }

            MarkerCount++;
            return Task.FromResult(true);
        }

        public string GetOrCreateAnonymousObjectToken(string objectType, string objectIdentity) => "book-1";

        public void Record(NovelSpeaker.Application.Observability.DiagnosticDefinition definition, NovelSpeaker.Application.Observability.DiagnosticFieldSet fields)
        {
        }
    }

    private sealed class FakeExportService : IDiagnosticSessionExportService
    {
        public string? DestinationPath { get; set; }
        public string? LastDestinationPath { get; private set; }

        public Task ExportLastEndedAsync(string destinationPath, CancellationToken cancellationToken)
        {
            LastDestinationPath = destinationPath;
            return Task.CompletedTask;
        }

        public Task ExportAsync(string sessionFilePath, string destinationPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeWindowCapture : IDiagnosticWindowCapture
    {
        public Task<DiagnosticAttachment> CaptureCurrentWindowAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DiagnosticAttachment("capture-one", DateTimeOffset.UtcNow, "image/png", 1, 1, [1]));
    }

    private sealed class FaultingWindowCapture : IDiagnosticWindowCapture
    {
        public Task<DiagnosticAttachment> CaptureCurrentWindowAsync(CancellationToken cancellationToken) =>
            throw new IOException("capture unavailable");
    }

    private sealed class FakeFileDialogs : IPresentationFileDialogService
    {
        public string? SavePath { get; init; }
        public string? SuggestedFileName { get; private set; }

        public Task<string?> PickOpenFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken)
        {
            SuggestedFileName = options.SuggestedFileName;
            return Task.FromResult(SavePath);
        }

        public Task<string?> PickFolderAsync(PresentationFolderDialogOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "NovelSpeakerTestLocal",
            TimeSpan.FromHours(8),
            "NovelSpeaker test local time",
            "NovelSpeaker test local time");
    }
}
