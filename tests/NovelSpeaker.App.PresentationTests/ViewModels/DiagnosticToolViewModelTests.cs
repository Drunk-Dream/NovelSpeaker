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
        Assert.Equal(2, viewModel.MarkerCount);
        Assert.Equal(1, viewModel.AttachmentCount);
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

    private static DiagnosticToolViewModel CreateViewModel(
        FakeSessionService sessions,
        FakeExportService? exports = null) =>
        new(
            sessions,
            exports ?? new FakeExportService { DestinationPath = "problem.zip" },
            new FakeWindowCapture(),
            new FakeFileDialogs { SavePath = "problem.zip" },
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
            Task.FromResult(Current);

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

        public void RecordProblemMarker() => MarkerCount++;

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

    private sealed class FakeFileDialogs : IPresentationFileDialogService
    {
        public string? SavePath { get; init; }

        public Task<string?> PickOpenFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(SavePath);

        public Task<string?> PickFolderAsync(PresentationFolderDialogOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
