using Microsoft.Extensions.Logging.Abstractions;
using NovelSpeaker.App.Desktop.Lifecycle;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Desktop;

public sealed class DesktopLifecycleCoordinatorTests
{
    [Fact]
    public async Task Minimize_close_hides_without_running_exit_guard_or_shutdown()
    {
        var fixture = CreateFixture(
            AppSettings.Default with
            {
                MainWindowCloseBehavior = MainWindowCloseBehavior.MinimizeToTray
            });
        await fixture.Coordinator.StartAsync(CancellationToken.None);

        await fixture.Coordinator.RequestMainWindowCloseAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Platform.HideCount);
        Assert.Equal(0, fixture.Guard.ConfirmCount);
        Assert.Equal(0, fixture.Shutdown.Count);
        Assert.False(fixture.Coordinator.IsExitApproved);
        await fixture.Coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Ask_close_exit_runs_guard_shutdown_and_closes_in_order()
    {
        var calls = new List<string>();
        var fixture = CreateFixture(
            AppSettings.Default with
            {
                MainWindowCloseBehavior = MainWindowCloseBehavior.AskEveryTime
            },
            calls);
        fixture.Platform.CloseChoice = DesktopCloseChoice.ExitApplication;
        await fixture.Coordinator.StartAsync(CancellationToken.None);

        await fixture.Coordinator.RequestMainWindowCloseAsync(CancellationToken.None);

        Assert.True(fixture.Coordinator.IsExitApproved);
        Assert.Equal(["start", "show", "prompt", "guard", "shutdown", "close"], calls);
        await fixture.Coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Rejected_exit_can_be_retried_and_never_closes_on_rejection()
    {
        var fixture = CreateFixture(AppSettings.Default);
        fixture.Guard.Result = false;
        await fixture.Coordinator.StartAsync(CancellationToken.None);

        await fixture.Coordinator.RequestExitAsync(CancellationToken.None);
        fixture.Guard.Result = true;
        await fixture.Coordinator.RequestExitAsync(CancellationToken.None);

        Assert.Equal(2, fixture.Guard.ConfirmCount);
        Assert.Equal(1, fixture.Shutdown.Count);
        Assert.Equal(1, fixture.Platform.CloseCount);
        await fixture.Coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Concurrent_exit_requests_share_guard_shutdown_and_close()
    {
        var fixture = CreateFixture(AppSettings.Default);
        var confirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Guard.PendingResult = confirmation.Task;
        await fixture.Coordinator.StartAsync(CancellationToken.None);

        var first = fixture.Coordinator.RequestExitAsync(CancellationToken.None);
        var second = fixture.Coordinator.RequestExitAsync(CancellationToken.None);
        confirmation.SetResult(true);
        await Task.WhenAll(first, second);

        Assert.Same(first, second);
        Assert.Equal(1, fixture.Guard.ConfirmCount);
        Assert.Equal(1, fixture.Shutdown.Count);
        Assert.Equal(1, fixture.Platform.CloseCount);
        await fixture.Coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Startup_setting_hides_after_tray_platform_is_initialized()
    {
        var calls = new List<string>();
        var fixture = CreateFixture(
            AppSettings.Default with { StartMinimizedToTray = true },
            calls);

        await fixture.Coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(["start", "hide"], calls);
        await fixture.Coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Default_startup_shows_after_tray_platform_is_initialized()
    {
        var calls = new List<string>();
        var fixture = CreateFixture(AppSettings.Default, calls);

        await fixture.Coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(["start", "show"], calls);
        await fixture.Coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Tray_playback_command_is_serialized_to_application_playback_port()
    {
        var fixture = CreateFixture(AppSettings.Default);
        fixture.Playback.Snapshot = PlaybackSnapshot.Idle with { State = PlaybackState.Paused };
        await fixture.Coordinator.StartAsync(CancellationToken.None);

        fixture.Platform.Raise(DesktopLifecycleCommand.TogglePlayback);
        await fixture.Playback.ResumeObserved.Task;

        Assert.Equal(1, fixture.Playback.ResumeCount);
        await fixture.Coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cancellation_callback_can_reenter_stop_and_platform_stops_once()
    {
        var fixture = CreateFixture(AppSettings.Default);
        Task? reentrantStop = null;
        fixture.Playback.BlockResumeUntilCancellation = true;
        fixture.Playback.OnResumeCancellation = () =>
            reentrantStop = fixture.Coordinator.StopAsync(CancellationToken.None);
        fixture.Playback.Snapshot = PlaybackSnapshot.Idle with { State = PlaybackState.Paused };
        await fixture.Coordinator.StartAsync(CancellationToken.None);
        fixture.Platform.Raise(DesktopLifecycleCommand.TogglePlayback);
        await fixture.Playback.ResumeObserved.Task;

        var firstStop = fixture.Coordinator.StopAsync(CancellationToken.None);
        await firstStop;

        Assert.Same(firstStop, reentrantStop);
        Assert.Equal(1, fixture.Platform.StopCount);
    }

    [Fact]
    public async Task Stop_starts_platform_cleanup_before_returning_shared_task()
    {
        var fixture = CreateFixture(AppSettings.Default);
        var platformStopGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Platform.StopGate = platformStopGate.Task;
        await fixture.Coordinator.StartAsync(CancellationToken.None);

        var stopTask = fixture.Coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Platform.StopCount);
        Assert.False(stopTask.IsCompleted);
        platformStopGate.SetResult();
        await stopTask;
        Assert.Equal(1, fixture.Platform.StopCount);
    }

    [Fact]
    public async Task Command_failure_log_contains_only_command_and_failure_type()
    {
        var logger = new RecordingLogger<DesktopLifecycleCoordinator>();
        var fixture = CreateFixture(AppSettings.Default, logger: logger);
        await fixture.Coordinator.StartAsync(CancellationToken.None);
        fixture.Platform.ShowException = new InvalidOperationException("secret-url-and-body");

        fixture.Platform.Raise(DesktopLifecycleCommand.ShowMainWindow);
        var entry = await logger.Entry.Task;

        Assert.Null(entry.Exception);
        Assert.Contains(nameof(DesktopLifecycleCommand.ShowMainWindow), entry.Message);
        Assert.Contains(nameof(InvalidOperationException), entry.Message);
        Assert.DoesNotContain("secret-url-and-body", entry.Message);
        await fixture.Coordinator.StopAsync(CancellationToken.None);
    }

    private static Fixture CreateFixture(
        AppSettings settings,
        List<string>? calls = null,
        Microsoft.Extensions.Logging.ILogger<DesktopLifecycleCoordinator>? logger = null)
    {
        calls ??= [];
        var platform = new FakePlatform(calls);
        var guard = new FakeGuard(calls);
        var shutdown = new FakeShutdown(calls);
        var playback = new FakePlaybackSession();
        var coordinator = new DesktopLifecycleCoordinator(
            new FakeSettingsService(settings),
            playback,
            guard,
            shutdown,
            platform,
            logger ?? NullLogger<DesktopLifecycleCoordinator>.Instance);
        return new Fixture(coordinator, platform, guard, shutdown, playback);
    }

    private sealed record Fixture(
        DesktopLifecycleCoordinator Coordinator,
        FakePlatform Platform,
        FakeGuard Guard,
        FakeShutdown Shutdown,
        FakePlaybackSession Playback);

    private sealed class FakePlatform(List<string> calls) : IDesktopLifecyclePlatform
    {
        public event EventHandler<DesktopLifecycleCommand>? CommandReceived;

        public DesktopCloseChoice CloseChoice { get; set; } = DesktopCloseChoice.Cancel;
        public int HideCount { get; private set; }
        public int CloseCount { get; private set; }
        public int StopCount { get; private set; }
        public Exception? ShowException { get; set; }
        public Task? StopGate { get; set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            calls.Add("stop");
            return StopGate ?? Task.CompletedTask;
        }

        public Task ShowMainWindowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShowException is not null)
            {
                throw ShowException;
            }

            calls.Add("show");
            return Task.CompletedTask;
        }

        public Task HideMainWindowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HideCount++;
            calls.Add("hide");
            return Task.CompletedTask;
        }

        public Task CloseMainWindowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseCount++;
            calls.Add("close");
            return Task.CompletedTask;
        }

        public Task<DesktopCloseChoice> PromptForCloseChoiceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("prompt");
            return Task.FromResult(CloseChoice);
        }

        public void Raise(DesktopLifecycleCommand command)
        {
            CommandReceived?.Invoke(this, command);
        }
    }

    private sealed class FakeGuard(List<string> calls) : IDesktopExitGuard
    {
        public bool Result { get; set; } = true;
        public Task<bool>? PendingResult { get; set; }
        public int ConfirmCount { get; private set; }

        public Task<bool> ConfirmExitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfirmCount++;
            calls.Add("guard");
            return PendingResult ?? Task.FromResult(Result);
        }
    }

    private sealed class FakeShutdown(List<string> calls) : IProcessShutdownRequest
    {
        public int Count { get; private set; }

        public void Configure(Func<CancellationToken, Task> shutdownAsync)
        {
            throw new NotSupportedException();
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            calls.Add("shutdown");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsService(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(
            AppSettingsUpdate update,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakePlaybackSession : IPlaybackSession
    {
        public PlaybackSnapshot Snapshot { get; set; } = PlaybackSnapshot.Idle;
        public PlaybackSnapshot CurrentSnapshot => Snapshot;
        public int ResumeCount { get; private set; }
        public bool BlockResumeUntilCancellation { get; set; }
        public Action? OnResumeCancellation { get; set; }
        public TaskCompletionSource ResumeObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public async Task ResumeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResumeCount++;
            if (!BlockResumeUntilCancellation)
            {
                ResumeObserved.TrySetResult();
                return;
            }

            var cancellationObserved = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                () =>
                {
                    OnResumeCancellation?.Invoke();
                    cancellationObserved.TrySetCanceled(cancellationToken);
                });
            ResumeObserved.TrySetResult();
            await cancellationObserved.Task;
        }

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public TaskCompletionSource<(string Message, Exception? Exception)> Entry { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entry.TrySetResult((formatter(state, exception), exception));
        }
    }
}
