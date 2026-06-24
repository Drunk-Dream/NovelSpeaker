using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class BookPlaybackCoordinatorTests
{
    [Fact]
    public async Task StartAsync_with_selected_rule_and_audio_result_enters_playing_state()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 12), CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("示例小说", coordinator.CurrentSnapshot.BookTitle);
        Assert.Equal("第一章 开始", coordinator.CurrentSnapshot.ChapterTitle);
        Assert.Equal(12, coordinator.CurrentSnapshot.SpeakSpeed);
        Assert.Equal("默认规则", coordinator.CurrentSnapshot.RuleName);
        Assert.Equal("audio-1.mp3", localCoordinator.LastStartedRequest?.FilePath);
        Assert.Single(audioProvider.Requests);
    }

    [Fact]
    public async Task PlaybackCompleted_advances_to_next_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(localCoordinator);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.RaiseCompleted();

        await WaitForAsync(() => coordinator.CurrentSnapshot.SegmentIndex == 1);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task PlaybackCompleted_moves_to_next_chapter_and_stops_at_book_end()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            book: CreateTwoChapterBook());

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.RaiseCompleted();

        await WaitForAsync(() => coordinator.CurrentSnapshot.ChapterIndex == 1);
        Assert.Equal("第二章 延续", coordinator.CurrentSnapshot.ChapterTitle);

        localCoordinator.RaiseCompleted();

        await WaitForAsync(() => coordinator.CurrentSnapshot.State == PlaybackState.Stopped);
        Assert.Equal("全书播放完成。", coordinator.CurrentSnapshot.Message);
    }

    [Fact]
    public async Task Pause_and_resume_keep_current_session()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(localCoordinator);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.SetPosition(420);

        await coordinator.PauseAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Paused, coordinator.CurrentSnapshot.State);
        Assert.Equal(420, coordinator.CurrentSnapshot.PositionMilliseconds);

        await coordinator.ResumeAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
    }

    [Fact]
    public async Task RetryCurrentSegment_replays_failed_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.Network, "网络失败。");
        audioProvider.EnqueueSuccess("audio-retry.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Equal(PlaybackState.Faulted, coordinator.CurrentSnapshot.State);

        await coordinator.RetryCurrentSegmentAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("audio-retry.mp3", localCoordinator.LastStartedRequest?.FilePath);
        Assert.Equal(2, audioProvider.Requests.Count);
    }

    [Fact]
    public async Task SkipCurrentSegment_moves_to_following_segment_after_failure()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        audioProvider.EnqueueFailure(TtsErrorKind.ServerError, "服务错误。");
        audioProvider.EnqueueSuccess("audio-skip.mp3");
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Equal(PlaybackState.Faulted, coordinator.CurrentSnapshot.State);

        await coordinator.SkipCurrentSegmentAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, coordinator.CurrentSnapshot.SegmentIndex);
    }

    [Fact]
    public async Task StartAsync_without_selected_rule_enters_recoverable_fault()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            selectedRuleProvider: new FakeSelectedTtsRuleProvider(null));

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);

        Assert.Equal(PlaybackState.Faulted, coordinator.CurrentSnapshot.State);
        Assert.Contains("TTS 规则", coordinator.CurrentSnapshot.Message);
        Assert.False(coordinator.CurrentSnapshot.CanRetry);
    }

    [Fact]
    public async Task AudioDecode_failure_invalidates_and_regenerates_current_segment_once()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakePlaybackAudioProvider();
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            audioProvider: audioProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        localCoordinator.RaiseFailed(PlaybackErrorKind.AudioDecode, "音频损坏。");

        await WaitForAsync(() => audioProvider.InvalidateCallCount == 1);
        await WaitForAsync(() => audioProvider.Requests.Count == 2);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
    }

    [Fact]
    public async Task ChangeRule_and_change_speed_restart_current_segment()
    {
        var localCoordinator = new FakeLocalAudioPlaybackCoordinator();
        var selectedRuleProvider = new FakeSelectedTtsRuleProvider(CreateRuleSelection(1, "默认规则"));
        selectedRuleProvider.RegisterSelectable(CreateRuleSelection(2, "备用规则"));
        await using var coordinator = CreateCoordinator(
            localCoordinator,
            selectedRuleProvider: selectedRuleProvider);

        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        await coordinator.ChangeRuleAsync(2, CancellationToken.None);

        Assert.Equal("备用规则", coordinator.CurrentSnapshot.RuleName);

        await coordinator.ChangeSpeedAsync(16, CancellationToken.None);

        Assert.Equal(16, coordinator.CurrentSnapshot.SpeakSpeed);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
    }

    private static PlaybackCoordinator CreateCoordinator(
        FakeLocalAudioPlaybackCoordinator localCoordinator,
        FakeBookPlaybackContentService? bookContentService = null,
        FakeSelectedTtsRuleProvider? selectedRuleProvider = null,
        FakePlaybackAudioProvider? audioProvider = null,
        PlaybackBookContent? book = null)
    {
        return new PlaybackCoordinator(
            bookContentService ?? new FakeBookPlaybackContentService(book ?? CreateBook()),
            selectedRuleProvider ?? new FakeSelectedTtsRuleProvider(CreateRuleSelection(1, "默认规则")),
            audioProvider ?? new FakePlaybackAudioProvider(),
            localCoordinator,
            new FakeReadingProgressStore(),
            new FakePrefetchScheduler());
    }

    private static PlaybackBookContent CreateBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                new PlaybackChapterContent(
                    0,
                    "第一章 开始",
                    [
                        new SpeechSegment(0, 0, 6, "第一段", "第一段"),
                        new SpeechSegment(1, 6, 6, "第二段", "第二段")
                    ])
            ]);
    }

    private static PlaybackBookContent CreateTwoChapterBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                new PlaybackChapterContent(
                    0,
                    "第一章 开始",
                    [new SpeechSegment(0, 0, 6, "第一段", "第一段")]),
                new PlaybackChapterContent(
                    1,
                    "第二章 延续",
                    [new SpeechSegment(0, 6, 6, "第二章 第一段", "第二章 第一段")])
            ]);
    }

    private static SelectedPlaybackRule CreateRuleSelection(long id, string name)
    {
        var rule = new HttpTtsRule(
            id,
            name,
            "https://example.com/tts?text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}",
            "audio/mpeg",
            null,
            null,
            null,
            false,
            null,
            $"{{\"name\":\"{name}\",\"url\":\"https://example.com/tts?text={{{{encodeURIComponent(speakText)}}}}&speed={{{{speakSpeed}}}}\"}}",
            true,
            TtsRuleCompatibilityStatus.Compatible,
            [],
            null,
            "2026-06-24T00:00:00.0000000Z",
            "2026-06-24T00:00:00.0000000Z");

        return new SelectedPlaybackRule(id, name, rule, rule.ToNormalizedRule());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out while waiting for the playback coordinator to update.");
    }

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent _book;

        public FakeBookPlaybackContentService(PlaybackBookContent book)
        {
            _book = book;
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult(bookId == _book.BookId ? _book : null);
        }
    }

    private sealed class FakeSelectedTtsRuleProvider : ISelectedTtsRuleProvider
    {
        private readonly Dictionary<long, SelectedPlaybackRule> _rules = [];

        public FakeSelectedTtsRuleProvider(SelectedPlaybackRule? selectedRule)
        {
            if (selectedRule is not null)
            {
                SelectedRule = selectedRule;
                _rules[selectedRule.RuleId] = selectedRule;
            }
        }

        public SelectedPlaybackRule? SelectedRule { get; private set; }

        public void RegisterSelectable(SelectedPlaybackRule rule)
        {
            _rules[rule.RuleId] = rule;
        }

        public Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SelectedRule);
        }

        public Task<SelectedPlaybackRule?> SelectRuleAsync(long ruleId, CancellationToken cancellationToken)
        {
            SelectedRule = _rules.GetValueOrDefault(ruleId);
            return Task.FromResult(SelectedRule);
        }
    }

    private sealed class FakePlaybackAudioProvider : IPlaybackAudioProvider
    {
        private readonly Queue<PlaybackAudioResult> _results = [];

        public List<PlaybackAudioRequest> Requests { get; } = [];

        public int InvalidateCallCount { get; private set; }

        public void EnqueueFailure(TtsErrorKind kind, string message)
        {
            _results.Enqueue(new PlaybackAudioResult(
                null,
                false,
                new TtsExecutionFailure(kind, message, null, null, null, null)));
        }

        public void EnqueueSuccess(string filePath)
        {
            _results.Enqueue(new PlaybackAudioResult(filePath, false, null));
        }

        public Task<PlaybackAudioResult> GetAudioAsync(PlaybackAudioRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_results.Count > 0)
            {
                return Task.FromResult(_results.Dequeue());
            }

            return Task.FromResult(new PlaybackAudioResult($"audio-{Requests.Count}.mp3", false, null));
        }

        public Task InvalidateAsync(PlaybackAudioRequest request, CancellationToken cancellationToken)
        {
            InvalidateCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLocalAudioPlaybackCoordinator : ILocalAudioPlaybackCoordinator
    {
        public LocalAudioPlaybackSnapshot CurrentSnapshot { get; private set; } = LocalAudioPlaybackSnapshot.Idle;

        public LocalAudioPlaybackRequest? LastStartedRequest { get; private set; }

        public event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

        public event EventHandler? PlaybackCompleted;

        public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

        public Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken)
        {
            LastStartedRequest = request;
            CurrentSnapshot = new LocalAudioPlaybackSnapshot(
                PlaybackState.Playing,
                request.DisplayTitle,
                request.BookId,
                request.ChapterIndex,
                request.SegmentIndex,
                request.ResumePositionMilliseconds,
                1800,
                null,
                request.IsUsingCache);
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            CurrentSnapshot = CurrentSnapshot with { State = PlaybackState.Playing };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            CurrentSnapshot = CurrentSnapshot with { State = PlaybackState.Paused };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            CurrentSnapshot = CurrentSnapshot with
            {
                State = PlaybackState.Stopped,
                PositionMilliseconds = 0
            };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public Task SeekAsync(long positionMilliseconds, CancellationToken cancellationToken)
        {
            CurrentSnapshot = CurrentSnapshot with { PositionMilliseconds = positionMilliseconds };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void RaiseCompleted()
        {
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseFailed(PlaybackErrorKind kind, string message)
        {
            PlaybackFailed?.Invoke(this, new PlaybackErrorEventArgs(kind, message));
        }

        public void SetPosition(long positionMilliseconds)
        {
            CurrentSnapshot = CurrentSnapshot with { PositionMilliseconds = positionMilliseconds };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
        }
    }

    private sealed class FakeReadingProgressStore : IReadingProgressStore
    {
        public List<PlaybackProgressUpdate> SavedProgress { get; } = [];

        public Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
        {
            SavedProgress.Add(progress);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePrefetchScheduler : IPrefetchScheduler
    {
        public List<(Guid SessionId, int RequestCount)> ScheduleCalls { get; } = [];

        public List<Guid> CancelledSessions { get; } = [];

        public Task ScheduleAsync(Guid sessionId, IReadOnlyList<PlaybackAudioRequest> requests, CancellationToken cancellationToken)
        {
            ScheduleCalls.Add((sessionId, requests.Count));
            return Task.CompletedTask;
        }

        public Task CancelAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            CancelledSessions.Add(sessionId);
            return Task.CompletedTask;
        }
    }
}
