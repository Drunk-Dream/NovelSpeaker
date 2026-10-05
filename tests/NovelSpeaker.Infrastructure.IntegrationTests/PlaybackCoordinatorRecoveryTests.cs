using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

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
        await WaitForPlayingAsync(coordinator);

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(333, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task StartAsync_with_explicit_position_ignores_saved_progress()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry(
                "book-1",
                0,
                1,
                6,
                333,
                DateTimeOffset.Parse("2026-06-25T00:00:00.0000000Z"))
        };
        await using var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);

        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
        Assert.Equal(0, readingProgressStore.StoredProgress?.SegmentIndex);
        Assert.Equal(0, readingProgressStore.StoredProgress?.AudioPositionMilliseconds);
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
        await WaitForPlayingAsync(coordinator);

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
        await WaitForPlayingAsync(coordinator);
        localCoordinator.SetPosition(240);

        await coordinator.NextSegmentAsync(CancellationToken.None);

        Assert.Equal(2, readingProgressStore.SavedProgress.Count);
        var saved = readingProgressStore.SavedProgress[0];
        Assert.Equal(0, saved.SegmentIndex);
        Assert.Equal(0, saved.CharacterOffset);
        Assert.Equal(240, saved.AudioPositionMilliseconds);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task OpenPausedAsync_restores_saved_progress_without_requesting_audio_until_resume()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
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
        await WaitForPlayingAsync(coordinator);

        Assert.Single(audioProvider.Requests);
        Assert.Equal(333, localCoordinator.LastStartedRequest?.ResumePositionMilliseconds);
    }

    [Fact]
    public async Task JumpToSegmentAsync_while_paused_updates_position_without_immediate_audio_request()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry(
                "book-1",
                0,
                0,
                0,
                0,
                DateTimeOffset.Parse("2026-06-25T00:00:00.0000000Z"))
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            readingProgressStore: readingProgressStore,
            book: CreateThreeSegmentBook());

        await coordinator.OpenPausedAsync(new OpenBookPlaybackRequest("book-1", 0, 0, 10), CancellationToken.None);
        Assert.Empty(audioProvider.Requests);

        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);

        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Empty(audioProvider.Requests);
        Assert.Equal(2, readingProgressStore.StoredProgress?.SegmentIndex);
    }

    [Fact]
    public async Task JumpToSegmentAsync_while_playing_persists_new_position_after_old_session_stops()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);
        await WaitForPlayingAsync(coordinator);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, readingProgressStore.StoredProgress?.SegmentIndex);
    }

    [Fact]
    public async Task JumpToSegmentAsync_when_committed_checkpoint_fails_keeps_target_and_can_resume()
    {
        var expected = new InvalidOperationException("checkpoint failed");
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry(
                "book-1",
                0,
                0,
                0,
                0,
                DateTimeOffset.Parse("2026-06-25T00:00:00.0000000Z")),
            SaveFailure = expected,
            SaveFailureCall = 3
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);
        await WaitForPlayingAsync(coordinator);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, readingProgressStore.StoredProgress?.SegmentIndex);

        await coordinator.ResumeAsync(CancellationToken.None);
        await WaitForPlayingAsync(coordinator);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task JumpToSegmentAsync_when_committed_checkpoint_is_cancelled_keeps_target()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var saveGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readingProgressStore = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry(
                "book-1",
                0,
                0,
                0,
                0,
                DateTimeOffset.Parse("2026-06-25T00:00:00.0000000Z")),
            SaveGate = saveGate,
            SaveGateCall = 2
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore,
            book: CreateThreeSegmentBook());

        await coordinator.OpenPausedAsync(
            new OpenBookPlaybackRequest("book-1", null, null, 10),
            CancellationToken.None);

        using var cancellationSource = new CancellationTokenSource();
        var jumpTask = coordinator.JumpToSegmentAsync(0, 2, cancellationSource.Token);
        var saveCall = await readingProgressStore.SecondSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, saveCall);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => jumpTask);

        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, readingProgressStore.StoredProgress?.SegmentIndex);

        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, readingProgressStore.StoredProgress?.SegmentIndex);
    }

    [Fact]
    public async Task Cancelled_committed_checkpoint_ignores_completion_queued_from_previous_session()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var saveGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readingProgressStore = new FakeReadingProgressStore
        {
            SaveGate = saveGate,
            SaveGateCall = 3
        };
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            readingProgressStore: readingProgressStore,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);
        await WaitForPlayingAsync(coordinator);

        using var cancellationSource = new CancellationTokenSource();
        var jumpTask = coordinator.JumpToSegmentAsync(0, 2, cancellationSource.Token);
        var saveCall = await readingProgressStore.ThirdSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, saveCall);
        localCoordinator.RaiseCompleted();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => jumpTask);
        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, readingProgressStore.StoredProgress?.SegmentIndex);
    }

    [Fact]
    public async Task JumpToSegmentAsync_keeps_committed_target_when_audio_preparation_fails_and_retry_uses_it()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        var readingProgressStore = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            readingProgressStore: readingProgressStore,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var previousTargetRevision = coordinator.CurrentSnapshot.TargetRevision;
        audioProvider.EnqueueException(new IOException("audio preparation failed"));

        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.State == PlaybackState.Faulted);

        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.True(coordinator.CurrentSnapshot.TargetRevision > previousTargetRevision);
        Assert.True(coordinator.CurrentSnapshot.CanRetry);
        Assert.Equal(1, localCoordinator.StopCallCount);
        Assert.Equal(2, readingProgressStore.StoredProgress?.SegmentIndex);

        await coordinator.RetryCurrentSegmentAsync(CancellationToken.None);
        await WaitForPlayingAsync(coordinator);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, localCoordinator.LastStartedRequest?.SegmentIndex);
    }

    [Fact]
    public async Task Resume_while_current_target_is_preparing_does_not_restart_its_preparation()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);
        await WaitForPlayingAsync(coordinator);

        var pendingAudio = audioProvider.EnqueuePendingSuccess("target.mp3");
        var jumpTask = coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);
        await pendingAudio.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await jumpTask.WaitAsync(TimeSpan.FromSeconds(5));
        var requestCount = audioProvider.Requests.Count;
        var targetRevision = coordinator.CurrentSnapshot.TargetRevision;

        Assert.Equal(PlaybackState.Preparing, coordinator.CurrentSnapshot.State);
        await coordinator.ResumeAsync(CancellationToken.None);

        Assert.Equal(requestCount, audioProvider.Requests.Count);
        Assert.Equal(targetRevision, coordinator.CurrentSnapshot.TargetRevision);
        Assert.Equal(PlaybackState.Preparing, coordinator.CurrentSnapshot.State);

        pendingAudio.CompleteSuccess();
        await WaitForPlayingAsync(coordinator);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task Pause_during_target_preparation_keeps_target_and_resume_reprepares_it()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        var readingProgressStore = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            readingProgressStore: readingProgressStore,
            book: CreateThreeSegmentBook());

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", 0, 0, null, 10),
            CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var pendingAudio = audioProvider.EnqueuePendingSuccess("target.mp3");
        var jumpTask = coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);
        await pendingAudio.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await jumpTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Preparing, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, localCoordinator.StopCallCount);
        await coordinator.PauseAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        pendingAudio.CompleteSuccess();
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(1, localCoordinator.StartCallCount);

        await coordinator.ResumeAsync(CancellationToken.None);
        await WaitForPlayingAsync(coordinator);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, localCoordinator.LastStartedRequest?.SegmentIndex);
    }

    [Fact]
    public async Task DisposeAsync_saves_current_progress_before_releasing_session()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var readingProgressStore = new FakeReadingProgressStore();
        var coordinator = CreateCoordinator(localCoordinator, readingProgressStore: readingProgressStore);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        localCoordinator.SetPosition(512);

        await coordinator.DisposeAsync();

        var saved = Assert.Single(readingProgressStore.SavedProgress);
        Assert.Equal(512, saved.AudioPositionMilliseconds);
        Assert.Equal(0, saved.CharacterOffset);
    }

    [Fact]
    public async Task Duplicate_playback_completed_events_advance_only_once()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
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
        await WaitForPlayingAsync(coordinator);

        localCoordinator.RaiseCompleted();
        localCoordinator.RaiseCompleted();

        await secondSegmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, audioProvider.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Automatic_next_commits_the_same_target_pipeline_for_cached_and_generated_audio(bool cached)
    {
        var local = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider();
        generation.EnqueueSuccess("first.mp3");
        if (cached) generation.EnqueueCachedSuccess("cached-next.mp3");
        else generation.EnqueueSuccess("generated-next.mp3");
        await using var coordinator = CreateCoordinator(local, audioProvider: generation, book: CreateThreeSegmentBook());
        var targetCommitted = new TaskCompletionSource<PlaybackSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.SegmentIndex == 1) targetCommitted.TrySetResult(snapshot);
        };

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var original = local.LastStartedRequest!;
        local.RaiseCompleted();

        var committed = await targetCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 &&
            coordinator.CurrentSnapshot.State == PlaybackState.Playing);

        Assert.Equal(1, committed.SegmentIndex);
        Assert.Equal(original.PlaybackSessionId, local.LastStartedRequest!.PlaybackSessionId);
        Assert.True(local.LastStartedRequest.TargetRevision > original.TargetRevision);
        Assert.Equal(cached, coordinator.CurrentSnapshot.IsUsingCache);
        Assert.Equal(2, generation.Requests.Count);
    }

    [Fact]
    public async Task Three_rapid_jumps_discard_late_preparations_and_play_only_the_latest_target()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateSegmentBook(4));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var sessionId = localCoordinator.LastStartedRequest!.PlaybackSessionId;

        var first = audioProvider.EnqueuePendingSuccess("late-first.mp3");
        var firstJump = coordinator.JumpToSegmentAsync(0, 1, CancellationToken.None);
        await first.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await firstJump.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Preparing, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, localCoordinator.StopCallCount);

        var second = audioProvider.EnqueuePendingSuccess("late-second.mp3");
        var secondJump = coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);
        await second.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await secondJump.WaitAsync(TimeSpan.FromSeconds(5));
        first.CompleteSuccess();

        var latest = audioProvider.EnqueuePendingSuccess("latest.mp3");
        var latestJump = coordinator.JumpToSegmentAsync(0, 3, CancellationToken.None);
        await latest.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await latestJump.WaitAsync(TimeSpan.FromSeconds(5));
        second.CompleteSuccess();
        latest.CompleteSuccess();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.State == PlaybackState.Playing &&
            coordinator.CurrentSnapshot.SegmentIndex == 3);

        Assert.Equal(3, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal([0, 1, 2, 3], audioProvider.Requests.Select(request => request.SegmentIndex));
        Assert.All(audioProvider.Requests, request => Assert.Equal(sessionId, request.SessionId));
        Assert.Equal(2, localCoordinator.StartCallCount);
        Assert.Equal(3, localCoordinator.LastStartedRequest?.SegmentIndex);
    }

    [Fact]
    public async Task Repeated_audio_decode_failure_invalidates_once_then_skips_the_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        audioProvider.EnqueueCachedSuccess("cached-corrupt.mp3");
        audioProvider.EnqueueSuccess("regenerated.mp3");
        audioProvider.EnqueueSuccess("next.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider,
            book: CreateThreeSegmentBook());
        var regenerated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State == PlaybackState.Playing &&
                snapshot.SegmentIndex == 0 && audioProvider.Requests.Count == 2)
            {
                regenerated.TrySetResult();
            }
        };

        await coordinator.StartAsync(
            new PlaybackStartRequest("book-1", null, null, null, 10),
            CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        localCoordinator.RaiseFailed(PlaybackErrorKind.AudioDecode, "缓存音频损坏。");

        await regenerated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, audioProvider.InvalidateCallCount);

        var nextPlaying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State == PlaybackState.Playing && snapshot.SegmentIndex == 1)
            {
                nextPlaying.TrySetResult();
            }
        };
        localCoordinator.RaiseFailed(PlaybackErrorKind.AudioDecode, "缓存音频再次损坏。");

        await nextPlaying.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(1, audioProvider.InvalidateCallCount);
    }

}
