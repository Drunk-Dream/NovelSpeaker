using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.UnitTests.Speech;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed partial class PlaybackCoordinatorTests
{
    [Fact]
    public async Task StartAsync_without_explicit_position_restores_saved_progress()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 0, 1, 6, 333, DateTimeOffset.Parse("2026-06-25T00:00:00.0000000Z"))
        };
        await using var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(333, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task StartAsync_with_explicit_position_ignores_saved_progress()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 0, 1, 6, 333, DateTimeOffset.Parse("2026-06-25T00:00:00.0000000Z"))
        };
        await using var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 0, null, 10), CancellationToken.None);

        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task StartAsync_remaps_saved_progress_by_character_offset_when_segment_index_is_missing()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 0, 8, 6, 333, DateTimeOffset.Parse("2026-06-25T00:00:00.0000000Z"))
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore,
            book: CreateRemappedBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task NextSegmentAsync_saves_previous_progress_before_switching_segments()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.SetPosition(240);

        await coordinator.NextSegmentAsync(CancellationToken.None);

        var saved = Assert.Single(readingProgressStore.SavedProgress);
        Assert.Equal(0, saved.SegmentIndex);
        Assert.Equal(0, saved.CharacterOffset);
        Assert.Equal(240, saved.AudioPositionMilliseconds);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task PreviousSegmentAsync_double_tap_while_playing_stops_intermediate_segment_before_buffering()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 2, null, 10), CancellationToken.None);
        await coordinator.PreviousSegmentAsync(CancellationToken.None);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);

        var pendingAudio = audioProvider.EnqueuePendingSuccess("audio-delayed-previous.mp3");
        var secondPreviousTask = coordinator.PreviousSegmentAsync(CancellationToken.None);

        await WaitForAsync(audioProvider, () => audioProvider.Requests.Count == 3);
        Assert.False(localCoordinator.TryRaiseCompleted());

        pendingAudio.CompleteSuccess();
        await secondPreviousTask;

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task PreviousSegmentAsync_after_pausing_keeps_paused_context_without_rebuffering()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 2, null, 10), CancellationToken.None);
        await coordinator.PauseAsync(CancellationToken.None);

        await coordinator.PreviousSegmentAsync(CancellationToken.None);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);

        var requestCountBeforeSecondJump = audioProvider.Requests.Count;
        await coordinator.PreviousSegmentAsync(CancellationToken.None);
        Assert.False(localCoordinator.TryRaiseCompleted());
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(requestCountBeforeSecondJump, audioProvider.Requests.Count);
    }

    [Fact]
    public async Task OpenPausedAsync_restores_saved_progress_without_requesting_audio_until_resume()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 0, 1, 6, 333, DateTimeOffset.Parse("2026-06-25T00:00:00.0000000Z"))
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            readingProgressStore: readingProgressStore);

        await coordinator.OpenPausedAsync(new OpenBookPlaybackRequest("book-1", null, null, 10), CancellationToken.None);

        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(333, coordinator.CurrentSnapshot.PositionMilliseconds);
        Assert.Empty(audioProvider.Requests);

        await coordinator.ResumeAsync(CancellationToken.None);

        Assert.Single(audioProvider.Requests);
        Assert.Equal(333, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task JumpToSegmentAsync_while_paused_updates_position_without_immediate_audio_request()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.OpenPausedAsync(new OpenBookPlaybackRequest("book-1", 0, 0, 10), CancellationToken.None);
        Assert.Empty(audioProvider.Requests);

        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);

        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Empty(audioProvider.Requests);
    }

    [Fact]
    public async Task JumpToSegmentAsync_while_playing_keeps_playing_and_requests_new_audio()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 0, null, 10), CancellationToken.None);
        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, audioProvider.Requests.Count);
    }

    [Fact]
    public async Task StartAsync_can_surface_cached_audio_usage_in_snapshot()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        audioProvider.EnqueueCachedSuccess("audio-cached.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.True(coordinator.CurrentSnapshot.IsUsingCache);
        Assert.Equal("audio-cached.mp3", localCoordinator.LastStartedRequest?.FilePath);
    }

    [Fact]
    public async Task DisposeAsync_saves_current_progress_before_releasing_session()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore();
        var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.SetPosition(512);

        await coordinator.DisposeAsync();

        var saved = Assert.Single(readingProgressStore.SavedProgress);
        Assert.Equal(512, saved.AudioPositionMilliseconds);
        Assert.Equal(0, saved.CharacterOffset);
    }

    [Fact]
    public async Task Playback_completed_dependency_failure_is_projected_without_leaking_event_task()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            SaveFailure = new InvalidOperationException("private progress failure")
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore);
        var faulted = new TaskCompletionSource<PlaybackSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State == PlaybackState.Faulted &&
                snapshot.Message == "播放事件处理失败，请稍后重试。")
            {
                faulted.TrySetResult(snapshot);
            }
        };

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", null, null, null, 10),
            CancellationToken.None);

        localCoordinator.RaiseCompleted();

        var snapshot = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PlaybackState.Faulted, snapshot.State);

        readingProgressStore.SaveFailure = null;
    }

    [Fact]
    public async Task DisposeAsync_blocks_late_completion_and_releases_local_player()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            SaveGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", null, null, null, 10),
            CancellationToken.None);

        var disposeTask = coordinator.DisposeAsync().AsTask();
        await readingProgressStore.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        localCoordinator.RaiseCompleted();
        readingProgressStore.SaveGate.SetResult(null);

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(localCoordinator.WasDisposed);
        Assert.Single(readingProgressStore.SavedProgress);
    }

    [Fact]
    public async Task Duplicate_playback_completed_events_advance_only_once()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());
        var secondSegmentStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State == PlaybackState.Playing && snapshot.SegmentIndex == 1)
            {
                secondSegmentStarted.TrySetResult(null);
            }
        };

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);

        localCoordinator.RaiseCompleted();
        localCoordinator.RaiseCompleted();

        await secondSegmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, audioProvider.Requests.Count);
    }

    [Fact]
    public async Task Completion_queued_before_immediate_jump_does_not_advance_the_jump_target_again()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);

        localCoordinator.RaiseCompleted();
        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);

        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task StopAsync_cancels_active_prefetch_session()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var prefetchScheduler = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            book: CreateThreeSegmentBook(),
            prefetchScheduler: prefetchScheduler);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        var sessionId = Assert.Single(prefetchScheduler.ScheduleCalls).SessionId;

        await coordinator.StopAsync(CancellationToken.None);

        Assert.Contains(sessionId, prefetchScheduler.CancelledSessions);
    }

    [Fact]
    public async Task Rapid_jump_commands_finish_at_the_latest_requested_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        var initialAudio = audioProvider.EnqueuePendingSuccess("delayed-initial.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        var startTask = coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);
        Assert.Single(audioProvider.Requests);

        var firstJump = coordinator.JumpToSegmentAsync(0, 1, CancellationToken.None);
        var latestJump = coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);
        initialAudio.CompleteSuccess();

        await Task.WhenAll(startTask, firstJump, latestJump).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal([0, 1, 2], audioProvider.Requests.Select(request => request.SegmentIndex));
        Assert.Equal(3, audioProvider.Requests.Select(request => request.SessionId).Distinct().Count());
        Assert.Equal(2, localCoordinator.StopCallCount);
    }

    [Fact]
    public async Task Repeated_audio_decode_failure_invalidates_only_once_and_enters_faulted_state()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        audioProvider.EnqueueCachedSuccess("cached-corrupt.mp3");
        audioProvider.EnqueueFailure(TtsErrorKind.AudioDecode, "重新生成的音频仍然损坏。");
        await using var coordinator = CreateCoordinator(localCoordinator, audioProvider: audioProvider);
        var faulted = new TaskCompletionSource<PlaybackSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State == PlaybackState.Faulted)
            {
                faulted.TrySetResult(snapshot);
            }
        };

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", null, null, null, 10),
            CancellationToken.None);
        localCoordinator.RaiseFailed(PlaybackErrorKind.AudioDecode, "缓存音频损坏。");

        var firstFailure = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("重新生成的音频仍然损坏。", firstFailure.Message);
        Assert.Equal(1, audioProvider.InvalidateCallCount);

        var secondFaulted = new TaskCompletionSource<PlaybackSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State == PlaybackState.Faulted && snapshot.Message == "缓存音频再次损坏。")
            {
                secondFaulted.TrySetResult(snapshot);
            }
        };
        localCoordinator.RaiseFailed(PlaybackErrorKind.AudioDecode, "缓存音频再次损坏。");

        var secondFailure = await secondFaulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PlaybackState.Faulted, secondFailure.State);
        Assert.True(secondFailure.CanRetry);
        Assert.Equal(1, audioProvider.InvalidateCallCount);
    }

    [Fact]
    public async Task Snapshot_subscriber_failure_is_observed_by_the_command_caller()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(localCoordinator);
        coordinator.SnapshotChanged += static (_, _) => throw new InvalidOperationException("subscriber failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(
            new PlaybackStartRequest("book-1", null, null, null, 10),
            CancellationToken.None));

        Assert.Equal("subscriber failed", exception.Message);
    }

}
