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
        await coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(2, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, readingProgressStore.StoredProgress?.SegmentIndex);
    }

    [Fact]
    public async Task JumpToSegmentAsync_when_new_checkpoint_fails_keeps_target_uncommitted()
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

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.JumpToSegmentAsync(0, 2, CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, readingProgressStore.StoredProgress?.SegmentIndex);

        await coordinator.ResumeAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task JumpToSegmentAsync_when_new_checkpoint_is_cancelled_keeps_previous_session()
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
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, readingProgressStore.StoredProgress?.SegmentIndex);

        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, readingProgressStore.StoredProgress?.SegmentIndex);
    }

    [Fact]
    public async Task Cancelled_jump_ignores_completion_queued_from_previous_session()
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

        using var cancellationSource = new CancellationTokenSource();
        var jumpTask = coordinator.JumpToSegmentAsync(0, 2, cancellationSource.Token);
        var saveCall = await readingProgressStore.ThirdSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, saveCall);
        localCoordinator.RaiseCompleted();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => jumpTask);
        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(0, readingProgressStore.StoredProgress?.SegmentIndex);
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

        localCoordinator.RaiseCompleted();
        localCoordinator.RaiseCompleted();

        await secondSegmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, audioProvider.Requests.Count);
    }

    [Fact]
    public async Task Rapid_jump_commands_finish_at_the_latest_requested_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
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
        var audioProvider = new FakeAudioGenerationProvider();
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

}
