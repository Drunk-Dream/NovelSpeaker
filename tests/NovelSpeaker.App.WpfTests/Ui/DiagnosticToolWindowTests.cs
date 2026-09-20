using System.Windows;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.TestKit.Wpf;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class DiagnosticToolWindowTests
{
    [Fact]
    public async Task Recovered_active_session_opens_as_a_compact_topmost_toolbar()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var sessions = new FakeSessionService(CreateSnapshot(DiagnosticSessionState.Active));
            var launcher = CreateLauncher(sessions);
            await launcher.RecoverAsync(CancellationToken.None);
            launcher.OpenIfSessionActive();

            var window = Assert.IsType<DiagnosticToolWindow>(launcher.CurrentWindow);
            using var host = new WpfWindowHost(window);
            try
            {
                window.UpdateLayout();

                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.True(window.Topmost);
                Assert.True(window.ActualWidth <= 1040);
                Assert.True(window.ActualHeight <= 150);
                Assert.Equal(
                    Visibility.Visible,
                    Assert.IsType<System.Windows.Controls.StackPanel>(window.FindName("CapturingActions")).Visibility);
                Assert.Equal(
                    Visibility.Collapsed,
                    Assert.IsType<System.Windows.Controls.StackPanel>(window.FindName("PreparingActions")).Visibility);
                Assert.Equal("2 MB / 256 MB", window.ViewModel.SessionStorageText);
                Assert.Contains("容量", window.ViewModel.StateText, StringComparison.Ordinal);

                await window.ViewModel.EndCommand.ExecuteAsync(null);
                Assert.Equal(DiagnosticToolState.Completed, window.ViewModel.State);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_session_or_ended_session_does_not_auto_open_the_toolbar(bool ended)
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var sessions = new FakeSessionService(
                ended ? CreateSnapshot(DiagnosticSessionState.Ended) : null);
            var launcher = CreateLauncher(sessions);

            await launcher.RecoverAsync(CancellationToken.None);
            launcher.OpenIfSessionActive();

            Assert.Null(launcher.CurrentWindow);
        });
    }

    private static DiagnosticToolLauncher CreateLauncher(FakeSessionService sessions) => new(
        new DiagnosticRecordingController(sessions),
        new FakeSessionExportService(),
        new FakeWindowCapture(),
        new FakeFileDialogs(),
        TimeProvider.System);

    private static DiagnosticSessionSnapshot CreateSnapshot(DiagnosticSessionState state) =>
        new(
            "session-1",
            state,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            state == DiagnosticSessionState.Ended
                ? new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero)
                : null,
            DiagnosticSessionCapacityPresets.LargeBytes,
            2 * 1024 * 1024,
            state == DiagnosticSessionState.Active,
            true,
            "process",
            state == DiagnosticSessionState.Active ? "hard-cap" : null);

    private sealed class FakeSessionService(DiagnosticSessionSnapshot? snapshot) : IDiagnosticSessionService
    {
        public DiagnosticSessionSnapshot? Current { get; private set; } =
            snapshot?.State == DiagnosticSessionState.Active ? snapshot : null;

        public DiagnosticSessionSnapshot? LastEnded { get; private set; } =
            snapshot?.State == DiagnosticSessionState.Ended ? snapshot : null;

        public Task<DiagnosticSessionSnapshot> StartAsync(
            DiagnosticSessionStartOptions options,
            CancellationToken cancellationToken)
        {
            Current = new DiagnosticSessionSnapshot(
                "session-2",
                DiagnosticSessionState.Active,
                DateTimeOffset.UtcNow,
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
            var current = Current ?? throw new InvalidOperationException();
            LastEnded = current with
            {
                State = DiagnosticSessionState.Ended,
                EndedAtUtc = DateTimeOffset.UtcNow
            };
            Current = null;
            return Task.FromResult(LastEnded);
        }

        public Task NotifyProcessShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddAttachmentAsync(DiagnosticAttachment attachment, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> RecordProblemMarkerAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public string GetOrCreateAnonymousObjectToken(string objectType, string objectIdentity) => "object-1";

        public void Record(DiagnosticDefinition definition, DiagnosticFieldSet fields)
        {
        }
    }

    private sealed class FakeSessionExportService : IDiagnosticSessionExportService
    {
        public Task ExportLastEndedAsync(string destinationPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ExportAsync(string sessionFilePath, string destinationPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeWindowCapture : IDiagnosticWindowCapture
    {
        public Task<DiagnosticAttachment> CaptureCurrentWindowAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DiagnosticAttachment("capture", DateTimeOffset.UtcNow, "image/png", 1, 1, [1]));
    }

    private sealed class FakeFileDialogs : IPresentationFileDialogService
    {
        public Task<string?> PickOpenFileAsync(
            PresentationFileDialogOptions options,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(
            PresentationFileDialogOptions options,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(
            PresentationFolderDialogOptions options,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
