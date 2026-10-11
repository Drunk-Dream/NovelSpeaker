using System.Collections.Concurrent;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.Cache;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed partial class PlaybackCoordinatorTests
{
    [Fact]
    public async Task Late_audio_snapshot_completion_and_failure_from_replaced_session_are_ignored()
    {
        var local = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider();
        var progress = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(local, audioProvider: generation, readingProgressStore: progress);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var oldAudio = local.CurrentSnapshot;

        var pendingAudio = generation.EnqueuePendingSuccess("replacement.mp3");
        var jump = coordinator.JumpToSegmentAsync(0, 1, CancellationToken.None);
        await pendingAudio.Started.WaitAsync(TimeSpan.FromSeconds(5));
        local.RaiseHistoricalSnapshot(oldAudio with { PositionMilliseconds = 700 });
        local.RaiseHistoricalCompleted(oldAudio with { State = PlaybackState.Stopped, PositionMilliseconds = 1800 });
        local.RaiseHistoricalFailed(oldAudio with { State = PlaybackState.Faulted }, PlaybackErrorKind.AudioDecode, "迟到的音频错误");
        pendingAudio.CompleteSuccess();
        await jump;
        await WaitForPlayingAsync(coordinator);

        var newAudio = local.CurrentSnapshot;
        Assert.Equal(oldAudio.PlaybackSessionId, newAudio.PlaybackSessionId);
        Assert.True(newAudio.TargetRevision > oldAudio.TargetRevision);
        Assert.Equal(1, newAudio.SegmentIndex);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);

        var acceptedProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSnapshotChanged(object? _, PlaybackSnapshot snapshot)
        {
            if (snapshot.SegmentIndex == 1 && snapshot.PositionMilliseconds == 123)
                acceptedProgress.TrySetResult();
        }
        coordinator.SnapshotChanged += OnSnapshotChanged;
        try
        {
            local.PublishSnapshot(newAudio with { PositionMilliseconds = 123 });
            await acceptedProgress.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            coordinator.SnapshotChanged -= OnSnapshotChanged;
        }

        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
        Assert.Equal(123, coordinator.CurrentSnapshot.PositionMilliseconds);
        Assert.DoesNotContain(progress.SavedProgress,
            update => update.SegmentIndex == 0 && update.AudioPositionMilliseconds == 1800);
    }

    [Theory]
    [InlineData("jump", true)]
    [InlineData("segment", true)]
    [InlineData("chapter", true)]
    [InlineData("jump", false)]
    [InlineData("segment", false)]
    [InlineData("chapter", false)]
    public async Task Source_invalidation_cancels_navigation_content_preparation(string command, bool removal)
    {
        var content = new FakeBookPlaybackContentService(CreateTwoChapterBook() with { SourceContext = new("source-1", "catalog-1") });
        var local = new FakeLocalAudioPlaybackCoordinator();
        var changes = new SourceChanges();
        await using var coordinator = CreateCoordinator(local, bookContentService: content, sourceChanges: changes,
            appSettingsStore: new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = 0 }));
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        content.ChapterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        content.ChapterRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigation = command switch
        {
            "segment" => coordinator.NextSegmentAsync(CancellationToken.None),
            "chapter" => coordinator.NextChapterAsync(CancellationToken.None),
            _ => coordinator.JumpToChapterAsync(1, CancellationToken.None)
        };
        await content.ChapterRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Task? stop = null;
            if (removal) stop = coordinator.StopForRemovalAsync("book-1", "source-1", CancellationToken.None);
            else
            {
                content.Book = content.Book with { SourceContext = new("source-1", "catalog-2") };
                changes.Publish(new BookCommittedChange.ActiveCatalogCommitted("book-1", "source-1", "catalog-2"));
            }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation.WaitAsync(TimeSpan.FromSeconds(5)));
            if (stop is not null) await stop.WaitAsync(TimeSpan.FromSeconds(5));
            else await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookId is null);
            Assert.Null(coordinator.CurrentSnapshot.BookId);
        }
        finally { content.ChapterGate.TrySetResult(); }
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("configuration")]
    [InlineData("none")]
    public async Task Provider_commit_during_synthesis_applies_before_the_sentence_starts(string change)
    {
        var original = CreateRuleSelection(1, "原服务");
        var latest = change == "none" ? null : change == "selection" ? CreateRuleSelection(2, "新服务") : original with
        {
            Provider = original.Provider with
            {
                Configuration = ((HttpSpeechProviderConfiguration)original.Provider.Configuration) with
                { UrlTemplate = "https://example.com/new?text={{encodeURIComponent(speakText)}}" }
            }
        };
        var providers = new FakeCurrentSpeechProvider(original);
        var generation = new FakeAudioGenerationProvider();
        var local = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(local, selectedProviderProvider: providers, audioProvider: generation);
        var pending = generation.EnqueuePendingSuccess("obsolete.mp3");
        var starting = coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(5));
        providers.CommitProvider(latest);
        pending.CompleteSuccess();
        await starting;

        if (latest is null)
        {
            await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.State == PlaybackState.Stopped);
            Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
            Assert.False(coordinator.CurrentSnapshot.HasAvailableProvider);
            Assert.Equal(0, local.StartCallCount);
            Assert.Single(generation.Requests);
        }
        else
        {
            await WaitForAsync(generation, () => generation.Requests.Count == 2);
            await WaitForPlayingAsync(coordinator);
            Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
            Assert.Same(latest, generation.Requests.Last().Provider);
            Assert.Equal(2, generation.Requests.Count);
            Assert.Equal(1, local.StartCallCount);
            Assert.NotEqual("obsolete.mp3", local.LastStartedRequest!.FilePath);
            Assert.Equal(generation.Requests.Last().SessionId, local.LastStartedRequest.PlaybackSessionId);
        }
    }

    [Fact]
    public async Task DisposeAsync_cancels_inflight_target_preparation_without_starting_audio()
    {
        var generation = new FakeAudioGenerationProvider { ObserveCancellation = true };
        var pending = generation.EnqueuePendingSuccess("pending-dispose.mp3");
        var local = new FakeLocalAudioPlaybackCoordinator();
        var coordinator = CreateCoordinator(local, audioProvider: generation);

        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        pending.CompleteSuccess();

        Assert.Equal(0, local.StartCallCount);
    }

    [Fact]
    public async Task Regex_remapping_commits_new_target_and_rejects_old_audio_identity()
    {
        var rule = SpeechRule("^第一段$", "第一段") with { Scope = RegexReplacementScope.Both };
        var repository = new PlaybackRegexRepository([rule]);
        var workspace = CreateRegexWorkspace(repository);
        var content = new RegexPlaybackContent(new FakeBookPlaybackContentService(CreateThreeSegmentBook() with
        { SourceContext = new("source-1", "catalog-1") }),
            new RegexReplacementPipeline(repository, new RegexReplacementRuleErrorStore()));
        var local = new FakeLocalAudioPlaybackCoordinator();
        var progress = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(local, bookContentService: content,
            regexWorkspace: workspace, readingProgressStore: progress);
        await coordinator.StartAsync(new("book-1", 0, 1, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var original = local.LastStartedRequest;
        await workspace.SaveEditorAsync(Editor(rule, ""), CancellationToken.None);
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 0 &&
            coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.NotSame(original, local.LastStartedRequest);
        Assert.Equal(original!.PlaybackSessionId, local.LastStartedRequest!.PlaybackSessionId);
        Assert.Equal(1, local.StopCallCount);
        local.SetPosition(777);
        await coordinator.PauseAsync(CancellationToken.None);
        Assert.Equal(777, progress.SavedProgress.Last().AudioPositionMilliseconds);
        Assert.Equal(6, progress.SavedProgress.Last().CharacterOffset);
        await coordinator.ResumeAsync(CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        local.RaiseCompleted();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 && coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.Equal(3, local.StartCallCount);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("open")]
    [InlineData("jump")]
    public async Task Content_preparation_failure_preserves_session_audio_and_checkpoint(string command)
    {
        var local = new FakeLocalAudioPlaybackCoordinator();
        var content = new FakeBookPlaybackContentService(CreateTwoChapterBook());
        var progress = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(local, bookContentService: content, readingProgressStore: progress,
            appSettingsStore: new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = 0 }));
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var before = coordinator.CurrentSnapshot;
        var request = local.LastStartedRequest;
        var saves = progress.SaveCallCount;
        content.ChapterFailure = new IOException("content-preparation-failed");

        await Assert.ThrowsAsync<IOException>(() => PrepareTargetAsync(coordinator, command, CancellationToken.None));

        Assert.Equal(before, coordinator.CurrentSnapshot);
        Assert.Same(request, local.LastStartedRequest);
        Assert.Equal(0, local.StopCallCount);
        Assert.Equal(saves, progress.SaveCallCount);
        content.ChapterFailure = null;
    }

    [Fact]
    public async Task Removal_cancels_opening_before_a_session_is_committed()
    {
        var content = new FakeBookPlaybackContentService(CreateBook() with { SourceContext = new("source-1", "catalog-1") })
        { ChapterGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var local = new FakeLocalAudioPlaybackCoordinator();
        var progress = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(local, bookContentService: content, readingProgressStore: progress);
        var opening = coordinator.OpenPausedAsync(new("book-1", null, null, null), CancellationToken.None);
        await content.ChapterRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var removal = coordinator.StopForRemovalAsync("book-1", "source-1", CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        await removal;

        Assert.Null(coordinator.CurrentSnapshot.BookId);
        Assert.Empty(progress.SavedProgress);
        Assert.Equal(0, local.StartCallCount);
    }

    private static Task PrepareTargetAsync(PlaybackCoordinator coordinator, string command, CancellationToken ct) => command switch
    {
        "start" => coordinator.StartAsync(new("book-1", 1, 0, null, 10), ct),
        "open" => coordinator.OpenPausedAsync(new("book-1", 1, 0, 10), ct),
        _ => coordinator.JumpToChapterAsync(1, ct)
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Source_invalidation_cancels_pending_corrupt_audio_regeneration(bool removal)
    {
        var local = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider { ObserveCancellation = true };
        var content = new FakeBookPlaybackContentService(CreateBook() with { SourceContext = new("source-1", "catalog-1") });
        var changes = new SourceChanges();
        await using var coordinator = CreateCoordinator(local, audioProvider: generation, bookContentService: content, sourceChanges: changes);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var pending = generation.EnqueuePendingSuccess("regenerated.mp3");
        local.RaiseFailed(PlaybackErrorKind.AudioDecode, "decode-failed");
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (removal) await coordinator.StopForRemovalAsync("book-1", "source-1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            else
            {
                content.Book = content.Book with { SourceContext = new("source-1", "catalog-2") };
                changes.Publish(new BookCommittedChange.ActiveCatalogCommitted("book-1", "source-1", "catalog-2"));
                await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookId is null);
            }
            Assert.Null(coordinator.CurrentSnapshot.BookId);
            Assert.Equal(1, local.StartCallCount);
            Assert.Equal(1, local.StopCallCount);
        }
        finally { pending.CompleteSuccess(); }
    }

    [Fact]
    public async Task Target_change_stops_old_audio_and_keeps_its_cache_protected_until_new_audio_starts()
    {
        var local = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider();
        var protection = new AudioCacheProtectionRegistry();
        var prefetch = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(local, audioProvider: generation,
            protectionRegistry: protection, prefetchScheduler: prefetch);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var old = local.LastStartedRequest!;
        var cancellations = prefetch.CancelledSessions.Count;
        var pending = generation.EnqueuePendingSuccess("replacement.mp3");
        var jump = coordinator.JumpToSegmentAsync(0, 1, CancellationToken.None);
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await jump.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(protection.IsProtected(old.FilePath));
        Assert.Equal(cancellations, prefetch.CancelledSessions.Count);
        Assert.Equal(1, local.StopCallCount);
        pending.CompleteSuccess();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 &&
            coordinator.CurrentSnapshot.State == PlaybackState.Playing);

        Assert.False(protection.IsProtected(old.FilePath));
        Assert.True(protection.IsProtected(local.LastStartedRequest!.FilePath));
        Assert.Equal(old.PlaybackSessionId, local.LastStartedRequest.PlaybackSessionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Committed_provider_change_keeps_current_audio_and_updates_next_sentence(bool clear)
    {
        var providers = new FakeCurrentSpeechProvider(CreateRuleSelection(1, "原服务"));
        var local = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider();
        var prefetch = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(local, selectedProviderProvider: providers,
            audioProvider: generation, prefetchScheduler: prefetch);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var old = local.LastStartedRequest;
        providers.CommitProvider(clear ? null : CreateRuleSelection(2, "新服务"));
        var scheduleCount = prefetch.ScheduleCalls.Count;
        // A user command provides a deterministic queue boundary after the committed fact.
        await coordinator.ChangeSpeedAsync(10, CancellationToken.None);
        await WaitForAsync(prefetch, () => prefetch.ScheduleCalls.Count > scheduleCount);
        Assert.Same(old, local.LastStartedRequest);
        Assert.Equal(0, local.StopCallCount);
        var window = prefetch.ScheduleCalls.Last().Requests;
        if (clear) Assert.Empty(window);
        else Assert.All(window, request => Assert.Equal(providers.SelectedProvider!.ProviderId, request.Provider.ProviderId));
        local.RaiseCompleted();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 &&
            coordinator.CurrentSnapshot.State == (clear ? PlaybackState.Stopped : PlaybackState.Playing));
        if (clear) Assert.Single(generation.Requests);
        else Assert.Equal(providers.SelectedProvider!.ProviderId, generation.Requests.Last().Provider.ProviderId);
    }
}
