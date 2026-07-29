using NovelSpeaker.Application.Desktop.MediaControls;
using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Desktop;

public sealed class MediaControlCoordinatorTests
{
    [Theory]
    [InlineData(MediaControlCommand.Play, "resume")]
    [InlineData(MediaControlCommand.Pause, "pause")]
    [InlineData(MediaControlCommand.Previous, "previous-segment")]
    [InlineData(MediaControlCommand.Next, "next-segment")]
    public async Task Platform_commands_map_to_playback_session_commands(
        MediaControlCommand command,
        string expectedCall)
    {
        var platform = new FakeMediaControlPlatform();
        var playback = new FakePlaybackSession();
        var reporter = new RecordingFailureReporter();
        await using var coordinator = new MediaControlCoordinator(platform, playback, reporter);
        await coordinator.StartAsync(CancellationToken.None);
        await platform.WaitForUpdatesAsync(1);

        platform.Raise(command);
        await playback.WaitForCallsAsync(1);

        Assert.Equal([expectedCall], playback.Calls);
        Assert.Empty(reporter.CommandFailures);
    }

    [Fact]
    public async Task Start_and_snapshot_changes_publish_projected_metadata()
    {
        var platform = new FakeMediaControlPlatform();
        var playback = new FakePlaybackSession
        {
            CurrentSnapshot = MediaControlMetadataProjectorTests.CreateSnapshot(
                PlaybackState.Paused,
                "第一章",
                "示例书")
        };
        await using var coordinator = new MediaControlCoordinator(
            platform,
            playback,
            new RecordingFailureReporter());

        await coordinator.StartAsync(CancellationToken.None);
        await platform.WaitForUpdatesAsync(1);
        playback.Publish(MediaControlMetadataProjectorTests.CreateSnapshot(
            PlaybackState.Playing,
            "第二章",
            "示例书"));
        await platform.WaitForUpdatesAsync(2);

        Assert.Equal(
            [
                new MediaControlMetadata("第一章", "示例书", MediaControlPlaybackStatus.Paused),
                new MediaControlMetadata("第二章", "示例书", MediaControlPlaybackStatus.Playing)
            ],
            platform.Updates);
    }

    [Fact]
    public async Task Progress_only_snapshots_do_not_repeat_unchanged_system_metadata()
    {
        var platform = new FakeMediaControlPlatform();
        var initial = MediaControlMetadataProjectorTests.CreateSnapshot(PlaybackState.Playing);
        var playback = new FakePlaybackSession { CurrentSnapshot = initial };
        await using var coordinator = new MediaControlCoordinator(
            platform,
            playback,
            new RecordingFailureReporter());
        await coordinator.StartAsync(CancellationToken.None);
        await platform.WaitForUpdatesAsync(1);

        playback.Publish(initial with { PositionMilliseconds = 500 });
        await coordinator.StopAsync(CancellationToken.None);

        Assert.Single(platform.Updates);
    }

    [Fact]
    public async Task Stop_unregisters_callbacks_and_stops_platform_once()
    {
        var platform = new FakeMediaControlPlatform();
        var playback = new FakePlaybackSession();
        await using var coordinator = new MediaControlCoordinator(
            platform,
            playback,
            new RecordingFailureReporter());
        await coordinator.StartAsync(CancellationToken.None);
        await platform.WaitForUpdatesAsync(1);

        await coordinator.StopAsync(CancellationToken.None);
        platform.Raise(MediaControlCommand.Next);
        playback.Publish(MediaControlMetadataProjectorTests.CreateSnapshot(PlaybackState.Playing));

        Assert.Empty(playback.Calls);
        Assert.Single(platform.Updates);
        Assert.Equal(1, platform.StartCalls);
        Assert.Equal(1, platform.StopCalls);
    }

    [Fact]
    public async Task Repeated_stop_requests_share_cleanup_and_unregister_once()
    {
        var platform = new FakeMediaControlPlatform();
        var playback = new FakePlaybackSession();
        await using var coordinator = new MediaControlCoordinator(
            platform,
            playback,
            new RecordingFailureReporter());
        await coordinator.StartAsync(CancellationToken.None);
        await platform.WaitForUpdatesAsync(1);

        await Task.WhenAll(
            coordinator.StopAsync(CancellationToken.None),
            coordinator.StopAsync(CancellationToken.None));

        Assert.Equal(1, platform.StopCalls);
    }

    [Fact]
    public async Task Stop_cancels_in_flight_command_and_allows_cancellation_callback_to_request_stop_again()
    {
        var platform = new FakeMediaControlPlatform();
        var playback = new FakePlaybackSession { BlockNextCommand = true };
        var reporter = new RecordingFailureReporter();
        await using var coordinator = new MediaControlCoordinator(platform, playback, reporter);
        Task? reentrantStop = null;
        playback.OnCommandCancellation = () =>
            reentrantStop = coordinator.StopAsync(CancellationToken.None);
        await coordinator.StartAsync(CancellationToken.None);
        await platform.WaitForUpdatesAsync(1);
        platform.Raise(MediaControlCommand.Next);
        await playback.CommandStarted;
        platform.Raise(MediaControlCommand.Pause);

        var firstStop = coordinator.StopAsync(CancellationToken.None);
        var repeatedStop = coordinator.StopAsync(CancellationToken.None);
        await playback.CommandCancellationObserved;
        await Task.WhenAll(firstStop, repeatedStop);
        Assert.NotNull(reentrantStop);
        await reentrantStop;

        Assert.Equal(["next-segment"], playback.Calls);
        Assert.Empty(reporter.CommandFailures);
        Assert.Equal(1, platform.StopCalls);
    }

    [Fact]
    public async Task Start_failure_unregisters_callbacks_and_releases_platform()
    {
        var platform = new FakeMediaControlPlatform { FailStart = true };
        var playback = new FakePlaybackSession();
        await using var coordinator = new MediaControlCoordinator(
            platform,
            playback,
            new RecordingFailureReporter());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(CancellationToken.None));
        platform.Raise(MediaControlCommand.Next);
        playback.Publish(MediaControlMetadataProjectorTests.CreateSnapshot(PlaybackState.Playing));

        Assert.Empty(playback.Calls);
        Assert.Empty(platform.Updates);
        Assert.Equal(1, platform.StopCalls);
    }

    [Fact]
    public async Task Command_and_metadata_failures_are_observed_without_stopping_later_callbacks()
    {
        var platform = new FakeMediaControlPlatform { FailNextUpdate = true };
        var playback = new FakePlaybackSession { FailNextCommand = true };
        var reporter = new RecordingFailureReporter();
        await using var coordinator = new MediaControlCoordinator(platform, playback, reporter);
        await coordinator.StartAsync(CancellationToken.None);
        await reporter.WaitForMetadataFailuresAsync(1);

        platform.Raise(MediaControlCommand.Next);
        await reporter.WaitForCommandFailuresAsync(1);
        platform.Raise(MediaControlCommand.Pause);
        await playback.WaitForCallsAsync(2);

        Assert.Equal(
            [MediaControlCommand.Next],
            reporter.CommandFailures.Select(static item => item.Command));
        Assert.Single(reporter.MetadataFailures);
        Assert.Equal(["next-segment", "pause"], playback.Calls);
    }

    private sealed class FakeMediaControlPlatform : IMediaControlPlatform
    {
        private readonly object _gate = new();
        private TaskCompletionSource _updatesChanged = CreateSignal();

        public event EventHandler<MediaControlCommand>? CommandReceived;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public bool FailNextUpdate { get; set; }

        public bool FailStart { get; set; }

        public List<MediaControlMetadata> Updates { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCalls++;
            if (FailStart)
            {
                throw new InvalidOperationException("start failed");
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(MediaControlMetadata metadata, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                throw new InvalidOperationException("update failed");
            }

            lock (_gate)
            {
                Updates.Add(metadata);
                _updatesChanged.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCalls++;
            return Task.CompletedTask;
        }

        public void Raise(MediaControlCommand command) =>
            CommandReceived?.Invoke(this, command);

        public async Task WaitForUpdatesAsync(int count)
        {
            while (true)
            {
                Task signal;
                lock (_gate)
                {
                    if (Updates.Count >= count)
                    {
                        return;
                    }

                    if (_updatesChanged.Task.IsCompleted)
                    {
                        _updatesChanged = CreateSignal();
                    }

                    signal = _updatesChanged.Task;
                }

                await signal;
            }
        }
    }

    private sealed class FakePlaybackSession : IPlaybackSession
    {
        private readonly object _gate = new();
        private TaskCompletionSource _callsChanged = CreateSignal();
        private readonly TaskCompletionSource _commandStarted = CreateSignal();
        private readonly TaskCompletionSource _commandCancellationObserved = CreateSignal();

        public PlaybackSnapshot CurrentSnapshot { get; set; } = PlaybackSnapshot.Idle;

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public bool FailNextCommand { get; set; }

        public bool BlockNextCommand { get; set; }

        public Action? OnCommandCancellation { get; set; }

        public List<string> Calls { get; } = [];

        public Task CommandStarted => _commandStarted.Task;

        public Task CommandCancellationObserved => _commandCancellationObserved.Task;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task ResumeAsync(CancellationToken cancellationToken) => RecordAsync("resume", cancellationToken);
        public Task PauseAsync(CancellationToken cancellationToken) => RecordAsync("pause", cancellationToken);
        public Task NextSegmentAsync(CancellationToken cancellationToken) => RecordAsync("next-segment", cancellationToken);
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => RecordAsync("previous-segment", cancellationToken);

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task NextChapterAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void SetVolume(double volume) => throw new NotSupportedException();

        public async Task WaitForCallsAsync(int count)
        {
            while (true)
            {
                Task signal;
                lock (_gate)
                {
                    if (Calls.Count >= count)
                    {
                        return;
                    }

                    if (_callsChanged.Task.IsCompleted)
                    {
                        _callsChanged = CreateSignal();
                    }

                    signal = _callsChanged.Task;
                }

                await signal;
            }
        }

        private async Task RecordAsync(string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                Calls.Add(name);
                _callsChanged.TrySetResult();
            }

            if (BlockNextCommand)
            {
                BlockNextCommand = false;
                var cancellationObserved = CreateSignal();
                using var registration = cancellationToken.Register(
                    () =>
                    {
                        OnCommandCancellation?.Invoke();
                        cancellationObserved.TrySetCanceled(cancellationToken);
                    });
                _commandStarted.TrySetResult();
                try
                {
                    await cancellationObserved.Task;
                }
                finally
                {
                    _commandCancellationObserved.TrySetResult();
                }
            }

            if (FailNextCommand)
            {
                FailNextCommand = false;
                throw new InvalidOperationException("command failed");
            }
        }
    }

    private sealed class RecordingFailureReporter : IMediaControlFailureReporter
    {
        private readonly object _gate = new();
        private TaskCompletionSource _commandFailuresChanged = CreateSignal();
        private TaskCompletionSource _metadataFailuresChanged = CreateSignal();

        public List<(MediaControlCommand Command, Exception Exception)> CommandFailures { get; } = [];

        public List<Exception> MetadataFailures { get; } = [];

        public void ReportCommandFailure(MediaControlCommand command, Exception exception)
        {
            lock (_gate)
            {
                CommandFailures.Add((command, exception));
                _commandFailuresChanged.TrySetResult();
            }
        }

        public void ReportMetadataFailure(Exception exception)
        {
            lock (_gate)
            {
                MetadataFailures.Add(exception);
                _metadataFailuresChanged.TrySetResult();
            }
        }

        public Task WaitForCommandFailuresAsync(int count) =>
            WaitAsync(() => CommandFailures.Count, count, true);

        public Task WaitForMetadataFailuresAsync(int count) =>
            WaitAsync(() => MetadataFailures.Count, count, false);

        private async Task WaitAsync(Func<int> getCount, int count, bool commands)
        {
            while (true)
            {
                Task signal;
                lock (_gate)
                {
                    if (getCount() >= count)
                    {
                        return;
                    }

                    ref var source = ref commands
                        ? ref _commandFailuresChanged
                        : ref _metadataFailuresChanged;
                    if (source.Task.IsCompleted)
                    {
                        source = CreateSignal();
                    }

                    signal = source.Task;
                }

                await signal;
            }
        }
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
