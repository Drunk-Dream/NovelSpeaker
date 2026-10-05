using System.Collections.Concurrent;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
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
            Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
            Assert.False(coordinator.CurrentSnapshot.HasAvailableProvider);
            Assert.Equal(0, local.StartCallCount);
            Assert.Single(generation.Requests);
        }
        else
        {
            Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
            Assert.Same(latest, generation.Requests.Last().Provider);
            Assert.Equal(2, generation.Requests.Count);
            Assert.Equal(1, local.StartCallCount);
            Assert.NotEqual("obsolete.mp3", local.LastStartedRequest!.FilePath);
            Assert.Equal(generation.Requests.Last().SessionId, local.LastStartedRequest.PlaybackSessionId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_commit_during_checkpoint_applies_before_audio_start(bool clear)
    {
        var providers = new FakeCurrentSpeechProvider(CreateRuleSelection(1, "原服务"));
        var latest = clear ? null : CreateRuleSelection(2, "新服务");
        var generation = new FakeAudioGenerationProvider();
        var local = new FakeLocalAudioPlaybackCoordinator();
        var progress = new FakeReadingProgressStore { SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var coordinator = CreateCoordinator(local, selectedProviderProvider: providers,
            audioProvider: generation, readingProgressStore: progress);
        var starting = coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await progress.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        providers.CommitProvider(latest);
        progress.SaveGate.SetResult(null);
        await starting;

        Assert.Equal(clear ? PlaybackState.Stopped : PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(clear ? 0 : 1, local.StartCallCount);
        Assert.Equal(clear ? 1 : 2, generation.Requests.Count);
        Assert.Equal(!clear, coordinator.CurrentSnapshot.HasAvailableProvider);
        if (!clear) Assert.Same(latest, generation.Requests.Last().Provider);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopped_context_consumes_provider_selection_and_none(bool clear)
    {
        var providers = new FakeCurrentSpeechProvider(CreateRuleSelection(1, "原服务"));
        var next = CreateRuleSelection(2, "新服务");
        providers.RegisterSelectable(next);
        var local = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(local, selectedProviderProvider: providers);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
        if (clear)
        {
            providers.CommitProvider(null);
            await coordinator.ResumeAsync(CancellationToken.None);
            Assert.False(coordinator.CurrentSnapshot.HasAvailableProvider);
            Assert.Null(coordinator.CurrentSnapshot.ProviderName);
        }
        else
        {
            await coordinator.ChangeProviderAsync(next.ProviderId, CancellationToken.None);
            Assert.Equal("新服务", coordinator.CurrentSnapshot.ProviderName);
        }
        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, local.StartCallCount);
        Assert.Equal(1, local.StopCallCount);
    }

    [Fact]
    public async Task Regex_remapping_retains_audio_callbacks_and_device_checkpoint_position()
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
        var original = local.LastStartedRequest;
        await workspace.SaveEditorAsync(Editor(rule, ""), CancellationToken.None);
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 0);
        Assert.Same(original, local.LastStartedRequest);
        Assert.Equal(0, local.StopCallCount);
        local.SetPosition(777);
        await coordinator.PauseAsync(CancellationToken.None);
        Assert.Equal(777, progress.SavedProgress.Last().AudioPositionMilliseconds);
        Assert.Equal(6, progress.SavedProgress.Last().CharacterOffset);
        await coordinator.ResumeAsync(CancellationToken.None);
        local.RaiseCompleted();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 && coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.Equal(2, local.StartCallCount);
    }

    [Fact]
    public async Task Stopped_context_consumes_regex_changes_before_resume()
    {
        var rule = SpeechRule("^第一段$", "原语音");
        var repository = new PlaybackRegexRepository([rule]);
        var workspace = CreateRegexWorkspace(repository);
        var generation = new FakeAudioGenerationProvider();
        var local = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(local, bookContentService: CreateRegexContent(repository),
            audioProvider: generation, regexWorkspace: workspace);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
        var revision = coordinator.CurrentSnapshot.ContentRevision;
        await workspace.SaveEditorAsync(Editor(rule, "新语音"), CancellationToken.None);
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.ContentRevision > revision);
        Assert.Equal(PlaybackState.Stopped, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, local.StartCallCount);
        await coordinator.ResumeAsync(CancellationToken.None);
        Assert.Equal("新语音", generation.Requests.Last().SpeechText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopped_regex_preparation_cancellation_does_not_publish_playback_failure(bool removal)
    {
        var repository = new PlaybackRegexRepository([]);
        var workspace = CreateRegexWorkspace(repository);
        var content = new FakeBookPlaybackContentService(CreateBook() with { SourceContext = new("source-1", "catalog-1") });
        var changes = new SourceChanges();
        var messages = new ConcurrentQueue<string?>();
        var settings = new FakeAppSettingsStore(AppSettings.Default);
        await using var coordinator = CreateCoordinator(new FakeLocalAudioPlaybackCoordinator(),
            bookContentService: content, regexWorkspace: workspace, sourceChanges: changes, appSettingsStore: settings);
        coordinator.SnapshotChanged += (_, snapshot) => messages.Enqueue(snapshot.Message);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
        content.ChapterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        content.ChapterRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await workspace.SaveEditorAsync(Editor(SpeechRule("^第一段$", "新语音"), "新语音") with { Id = null }, CancellationToken.None);
        await content.ChapterRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (removal) await coordinator.StopForRemovalAsync("book-1", "source-1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            else
            {
                content.Book = content.Book with { SourceContext = new("source-1", "catalog-2") };
                changes.Publish(new BookCommittedChange.ActiveCatalogCommitted("book-1", "source-1", "catalog-2"));
                await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookId is null);
            }
            // Once the first event is observed, the processor has finished the
            // cancelled Regex handler. The second event follows any failure it queued.
            settings.ReplaceSnapshot(settings.Current with { DefaultSpeakSpeed = 71 });
            await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SpeakSpeed == 71);
            settings.ReplaceSnapshot(settings.Current with { DefaultSpeakSpeed = 72 });
            await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SpeakSpeed == 72);
            Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
            Assert.DoesNotContain("播放事件处理失败，请稍后重试。", messages);
        }
        finally { content.ChapterGate.TrySetResult(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Configuration_fact_prefetch_cancellation_does_not_publish_playback_failure(bool providerChange, bool removal)
    {
        var content = new FakeBookPlaybackContentService(CreateTwoChapterBook() with { SourceContext = new("source-1", "catalog-1") });
        var changes = new SourceChanges();
        var providers = new FakeCurrentSpeechProvider(CreateRuleSelection(1, "原服务"));
        var settings = new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = 0 });
        var messages = new ConcurrentQueue<string?>();
        await using var coordinator = CreateCoordinator(new FakeLocalAudioPlaybackCoordinator(), bookContentService: content,
            sourceChanges: changes, selectedProviderProvider: providers, appSettingsStore: settings);
        coordinator.SnapshotChanged += (_, snapshot) => messages.Enqueue(snapshot.Message);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        // A prefetch-count-only settings fact does not enqueue a speed update.
        await settings.UpdateAsync(new AppSettingsUpdate { PrefetchCount = 1 }, CancellationToken.None);
        content.ChapterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        content.ChapterRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (providerChange) providers.CommitProvider(CreateRuleSelection(2, "新服务"));
        else settings.ReplaceSnapshot(settings.Current with { DefaultSpeakSpeed = 71 });
        await content.ChapterRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (removal) await coordinator.StopForRemovalAsync("book-1", "source-1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            else
            {
                content.Book = content.Book with { SourceContext = new("source-1", "catalog-2") };
                changes.Publish(new BookCommittedChange.ActiveCatalogCommitted("book-1", "source-1", "catalog-2"));
                await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookId is null);
            }
            settings.ReplaceSnapshot(settings.Current with { DefaultSpeakSpeed = 73 });
            await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SpeakSpeed == 73);
            settings.ReplaceSnapshot(settings.Current with { DefaultSpeakSpeed = 74 });
            await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SpeakSpeed == 74);
            Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
            Assert.DoesNotContain("播放事件处理失败，请稍后重试。", messages);
        }
        finally { content.ChapterGate.TrySetResult(); }
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

    [Theory]
    [InlineData("start")]
    [InlineData("open")]
    [InlineData("jump")]
    public async Task Ignored_content_cancellation_cannot_commit_a_target(string command)
    {
        var local = new FakeLocalAudioPlaybackCoordinator();
        var content = new FakeBookPlaybackContentService(CreateTwoChapterBook());
        var progress = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(local, bookContentService: content, readingProgressStore: progress,
            appSettingsStore: new FakeAppSettingsStore(AppSettings.Default with { PrefetchCount = 0 }));
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        var before = coordinator.CurrentSnapshot;
        var saves = progress.SaveCallCount;
        content.ChapterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        content.ChapterRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        content.IgnoreChapterCancellation = true;
        using var cancellation = new CancellationTokenSource();
        var preparation = PrepareTargetAsync(coordinator, command, cancellation.Token);
        await content.ChapterRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        content.ChapterGate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);

        Assert.Equal(before, coordinator.CurrentSnapshot);
        Assert.Equal(0, local.StopCallCount);
        Assert.Equal(saves, progress.SaveCallCount);
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

    [Fact]
    public async Task Metadata_commit_during_opening_is_consumed_after_commit()
    {
        var content = new FakeBookPlaybackContentService(CreateBook())
        { ChapterGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var changes = new SourceChanges();
        await using var coordinator = CreateCoordinator(new FakeLocalAudioPlaybackCoordinator(), bookContentService: content, sourceChanges: changes);
        var opening = coordinator.OpenPausedAsync(new("book-1", null, null, null), CancellationToken.None);
        await content.ChapterRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        content.Book = content.Book with { BookTitle = "已更新", BookAuthor = "新作者" };
        changes.Publish(new BookCommittedChange.MetadataCommitted("book-1"));
        content.ChapterGate.SetResult();
        await opening;
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookTitle == "已更新");

        Assert.Equal("新作者", coordinator.CurrentSnapshot.BookAuthor);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
    }

    [Fact]
    public async Task Settings_replacement_updates_next_sentence_speed_without_interrupting_current_audio()
    {
        var settings = new FakeAppSettingsStore(AppSettings.Default);
        var local = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(local, audioProvider: generation, appSettingsStore: settings);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        var request = local.LastStartedRequest;

        settings.ReplaceSnapshot(settings.Current with { DefaultSpeakSpeed = 72 });
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SpeakSpeed == 72);

        Assert.Same(request, local.LastStartedRequest);
        Assert.Equal(0, local.StopCallCount);
        local.RaiseCompleted();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 && coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.Equal(72, generation.Requests.Last().SpeakSpeed);
    }

    private static Task PrepareTargetAsync(PlaybackCoordinator coordinator, string command, CancellationToken ct) => command switch
    {
        "start" => coordinator.StartAsync(new("book-1", 1, 0, null, 10), ct),
        "open" => coordinator.OpenPausedAsync(new("book-1", 1, 0, 10), ct),
        _ => coordinator.JumpToChapterAsync(1, ct)
    };

    [Fact]
    public async Task Timer_survives_normal_sentence_completion_and_pauses_the_next_sentence()
    {
        var clock = new ManualTimeProvider();
        var local = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(local, timeProvider: clock);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        var timer = (IPlaybackStopTimer)coordinator;
        timer.ScheduleAfter(TimeSpan.FromMinutes(30));

        local.RaiseCompleted();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 && coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.Equal(PlaybackStopTimerMode.Duration, timer.CurrentSnapshot.Mode);
        clock.Advance(TimeSpan.FromMinutes(30));
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.State == PlaybackState.Paused);

        Assert.Equal(1, local.PauseCallCount);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Theory]
    [InlineData("jump", false, false)]
    [InlineData("jump", true, false)]
    [InlineData("regex", false, false)]
    [InlineData("regex", true, false)]
    [InlineData("jump", false, true)]
    [InlineData("regex", false, true)]
    public async Task New_sentence_after_provider_commit_uses_latest_provider_or_none(string command, bool clear, bool sameProvider)
    {
        var providers = new FakeCurrentSpeechProvider(CreateRuleSelection(1, "原服务"));
        var local = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider();
        var rule = SpeechRule("^第一段$", "原语音");
        var repository = new PlaybackRegexRepository([rule]);
        var workspace = CreateRegexWorkspace(repository);
        await using var coordinator = CreateCoordinator(local, selectedProviderProvider: providers,
            audioProvider: generation, bookContentService: CreateRegexContent(repository), regexWorkspace: workspace);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        var beforeRevision = coordinator.CurrentSnapshot.ContentRevision;
        providers.CommitProvider(clear ? null : CreateRuleSelection(sameProvider ? 1 : 2, "新服务"));

        if (command == "jump") await coordinator.JumpToSegmentAsync(0, 1, CancellationToken.None);
        else
        {
            await workspace.SaveEditorAsync(Editor(rule, "新语音"), CancellationToken.None);
            await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.ContentRevision == beforeRevision + 1 &&
                coordinator.CurrentSnapshot.State == (clear ? PlaybackState.Stopped : PlaybackState.Playing));
        }

        if (clear)
        {
            Assert.Single(generation.Requests);
            Assert.False(coordinator.CurrentSnapshot.HasAvailableProvider);
        }
        else
        {
            Assert.Equal(2, generation.Requests.Count);
            Assert.Same(providers.SelectedProvider, generation.Requests.Last().Provider);
        }
    }

    [Fact]
    public async Task Completion_checkpoint_failure_still_retires_prefetch_and_audio_protection()
    {
        var progress = new FakeReadingProgressStore { SaveFailure = new IOException("end-checkpoint-failed"), SaveFailureCall = 2 };
        var local = new FakeLocalAudioPlaybackCoordinator();
        var prefetch = new FakePrefetchScheduler();
        var protection = new AudioCacheProtectionRegistry();
        await using var coordinator = CreateCoordinator(local, readingProgressStore: progress,
            prefetchScheduler: prefetch, protectionRegistry: protection);
        await coordinator.StartAsync(new("book-1", 0, 1, null, 10), CancellationToken.None);
        var request = local.LastStartedRequest!;
        Assert.True(protection.IsProtected(request.FilePath));

        local.RaiseCompleted();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.Message == "播放事件处理失败，请稍后重试。");

        Assert.False(protection.IsProtected(request.FilePath));
        Assert.Contains(request.PlaybackSessionId!.Value, prefetch.CancelledSessions);
        progress.SaveFailure = null;
    }

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
    public async Task Empty_regex_target_checkpoint_failure_keeps_a_valid_stopped_projection()
    {
        var rule = SpeechRule(".*", "原语音");
        var repository = new PlaybackRegexRepository([rule]);
        var workspace = CreateRegexWorkspace(repository);
        var local = new FakeLocalAudioPlaybackCoordinator();
        var progress = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(local, readingProgressStore: progress,
            bookContentService: CreateRegexContent(repository), regexWorkspace: workspace);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        progress.SaveFailure = new IOException("empty-target-checkpoint-failed");
        var committedEmpty = new TaskCompletionSource<PlaybackSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.Message == "播放切换未完成，已保留当前目标。") committedEmpty.TrySetResult(snapshot);
        };
        await workspace.SaveEditorAsync(Editor(rule, ""), CancellationToken.None);
        var empty = await committedEmpty.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PlaybackState.Stopped, empty.State);
        Assert.Equal(0, empty.SegmentCount);
        Assert.False(empty.HasLoadedAudio);
        Assert.Equal(1, local.StopCallCount);
        progress.SaveFailure = null;
    }

    [Fact]
    public async Task Cancelled_synthesis_keeps_old_audio_protected_until_successful_replacement()
    {
        var local = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider();
        var protection = new AudioCacheProtectionRegistry();
        var prefetch = new FakePrefetchScheduler();
        await using var coordinator = CreateCoordinator(local, audioProvider: generation,
            protectionRegistry: protection, prefetchScheduler: prefetch);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        var old = local.LastStartedRequest!;
        var cancellations = prefetch.CancelledSessions.Count;
        var pending = generation.EnqueuePendingSuccess("replacement.mp3");
        using var cancellation = new CancellationTokenSource();
        var jump = coordinator.JumpToSegmentAsync(0, 1, cancellation.Token);
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(protection.IsProtected(old.FilePath));
        Assert.Equal(cancellations, prefetch.CancelledSessions.Count);
        cancellation.Cancel();
        pending.CompleteSuccess();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => jump);
        Assert.True(protection.IsProtected(old.FilePath));
        Assert.Same(old, local.LastStartedRequest);

        await coordinator.JumpToSegmentAsync(0, 1, CancellationToken.None);

        Assert.False(protection.IsProtected(old.FilePath));
        Assert.True(protection.IsProtected(local.LastStartedRequest!.FilePath));
        Assert.Contains(old.PlaybackSessionId!.Value, prefetch.CancelledSessions);
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
        var old = local.LastStartedRequest;
        providers.CommitProvider(clear ? null : CreateRuleSelection(2, "新服务"));
        // A user command provides a deterministic queue boundary after the committed fact.
        await coordinator.ChangeSpeedAsync(10, CancellationToken.None);
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
