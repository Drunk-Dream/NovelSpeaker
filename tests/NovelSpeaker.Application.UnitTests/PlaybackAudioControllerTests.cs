using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackAudioControllerTests
{
    [Fact]
    public async Task Delegates_audio_commands_and_bridges_callbacks()
    {
        var localAudio = new FakeLocalAudioPlaybackCoordinator();
        await using var controller = new PlaybackAudioController(localAudio);
        LocalAudioPlaybackSnapshot? observedSnapshot = null;
        var completed = false;
        PlaybackErrorEventArgs? observedError = null;
        controller.SnapshotChanged += (sender, snapshot) =>
        {
            Assert.Same(controller, sender);
            observedSnapshot = snapshot;
        };
        controller.PlaybackCompleted += (sender, _) =>
        {
            Assert.Same(controller, sender);
            completed = true;
        };
        controller.PlaybackFailed += (sender, error) =>
        {
            Assert.Same(controller, sender);
            observedError = error;
        };

        var request = new LocalAudioPlaybackRequest(
            "demo.wav",
            "测试音频",
            "book-1",
            0,
            1,
            120,
            true,
            Guid.NewGuid());
        await controller.StartAsync(request, CancellationToken.None);
        await controller.PauseAsync(CancellationToken.None);
        await controller.ResumeAsync(CancellationToken.None);
        await controller.SeekAsync(300, CancellationToken.None);
        await controller.StopAsync(CancellationToken.None);
        controller.SetVolume(0.35);
        localAudio.RaiseCompleted();
        var expectedError = new PlaybackErrorEventArgs(PlaybackErrorKind.AudioDecode, "解码失败");
        localAudio.RaiseFailed(expectedError);

        Assert.Same(expectedError, observedError);
        Assert.True(completed);
        Assert.Equal("book-1", observedSnapshot?.BookId);
        Assert.Equal(1, observedSnapshot?.SegmentIndex);
        Assert.Equal(request, localAudio.LastRequest);
        Assert.Equal(1, localAudio.PauseCallCount);
        Assert.Equal(1, localAudio.ResumeCallCount);
        Assert.Equal(1, localAudio.SeekCallCount);
        Assert.Equal(1, localAudio.StopCallCount);
        Assert.Equal(0.35, controller.Volume);
    }

    [Fact]
    public async Task Disposal_detaches_callbacks_and_disposes_local_audio_once()
    {
        var localAudio = new FakeLocalAudioPlaybackCoordinator();
        await using var controller = new PlaybackAudioController(localAudio);
        var callbackCount = 0;
        controller.SnapshotChanged += (_, _) => callbackCount++;

        await controller.DisposeAsync();
        localAudio.RaiseSnapshot();
        await controller.DisposeAsync();

        Assert.Equal(0, callbackCount);
        Assert.Equal(1, localAudio.DisposeCallCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            controller.PauseAsync(CancellationToken.None));
    }

    private sealed class FakeLocalAudioPlaybackCoordinator : ILocalAudioPlaybackCoordinator
    {
        public LocalAudioPlaybackSnapshot CurrentSnapshot { get; private set; } = LocalAudioPlaybackSnapshot.Idle;

        public double Volume { get; private set; } = PlaybackVolume.Default;

        public LocalAudioPlaybackRequest? LastRequest { get; private set; }

        public int PauseCallCount { get; private set; }

        public int ResumeCallCount { get; private set; }

        public int SeekCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

        public event EventHandler? PlaybackCompleted;

        public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

        public Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            CurrentSnapshot = new LocalAudioPlaybackSnapshot(
                PlaybackState.Playing,
                request.DisplayTitle,
                request.BookId,
                request.ChapterIndex,
                request.SegmentIndex,
                request.ResumePositionMilliseconds,
                1000,
                null,
                request.IsUsingCache,
                Volume,
                request.PlaybackSessionId);
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            ResumeCallCount++;
            CurrentSnapshot = CurrentSnapshot with { State = PlaybackState.Playing };
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            PauseCallCount++;
            CurrentSnapshot = CurrentSnapshot with { State = PlaybackState.Paused };
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCallCount++;
            CurrentSnapshot = CurrentSnapshot with { State = PlaybackState.Stopped };
            return Task.CompletedTask;
        }

        public Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken)
        {
            SeekCallCount++;
            CurrentSnapshot = CurrentSnapshot with { PositionMilliseconds = positionMilliseconds };
            return Task.CompletedTask;
        }

        public void SetVolume(double volume)
        {
            Volume = PlaybackVolume.Normalize(volume);
            CurrentSnapshot = CurrentSnapshot with { Volume = Volume };
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }

        public void RaiseSnapshot()
        {
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
        }

        public void RaiseCompleted()
        {
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseFailed(PlaybackErrorEventArgs error)
        {
            PlaybackFailed?.Invoke(this, error);
        }
    }
}
