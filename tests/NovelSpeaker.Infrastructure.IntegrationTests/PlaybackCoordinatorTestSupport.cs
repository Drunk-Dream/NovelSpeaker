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
    private static PlaybackCoordinator CreateCoordinator(
        FakeLocalAudioPlaybackCoordinator localCoordinator,
        FakeBookPlaybackContentService? bookContentService = null,
        FakeSelectedTtsRuleProvider? selectedRuleProvider = null,
        FakePlaybackAudioProvider? audioProvider = null,
        PlaybackBookContent? book = null,
        FakeReadingProgressStore? readingProgressStore = null,
        FakePrefetchScheduler? prefetchScheduler = null,
        FakeAppSettingsStore? appSettingsStore = null)
    {
        return new PlaybackCoordinator(
            bookContentService ?? new FakeBookPlaybackContentService(book ?? CreateBook()),
            selectedRuleProvider ?? new FakeSelectedTtsRuleProvider(CreateRuleSelection(1, "默认规则")),
            new PlaybackSegmentRunner(
                audioProvider ?? new FakePlaybackAudioProvider(),
                localCoordinator),
            new PlaybackRecoveryPolicy(),
            new AudioCacheProtectionRegistry(),
            localCoordinator,
            new PlaybackProgressService(readingProgressStore ?? new FakeReadingProgressStore()),
            prefetchScheduler ?? new FakePrefetchScheduler(),
            appSettingsStore ?? new FakeAppSettingsStore(AppSettings.Default));
    }

    private static PlaybackBookContent CreateBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                PlaybackChapterContent.FromLoaded(
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
                PlaybackChapterContent.FromLoaded(
                    0,
                    "第一章 开始",
                    [new SpeechSegment(0, 0, 6, "第一段", "第一段")]),
                PlaybackChapterContent.FromLoaded(
                    1,
                    "第二章 延续",
                    [new SpeechSegment(0, 6, 6, "第二章 第一段", "第二章 第一段")])
            ]);
    }

    private static PlaybackBookContent CreateThreeSegmentBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                PlaybackChapterContent.FromLoaded(
                    0,
                    "第一章 开始",
                    [
                        new SpeechSegment(0, 0, 6, "第一段", "第一段"),
                        new SpeechSegment(1, 6, 6, "第二段", "第二段"),
                        new SpeechSegment(2, 12, 6, "第三段", "第三段")
                    ])
            ]);
    }

    private static PlaybackBookContent CreateRemappedBook()
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [
                PlaybackChapterContent.FromLoaded(
                    0,
                    "第一章 开始",
                    [
                        new SpeechSegment(0, 0, 3, "甲段", "甲段"),
                        new SpeechSegment(1, 6, 3, "乙段", "乙段")
                    ])
            ]);
    }

    private static SelectedPlaybackRule CreateRuleSelection(long id, string name)
    {
        var rule = TestHttpTtsRules.Create(
            id,
            name,
            "https://example.com/tts?text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}",
            "audio/mpeg",
            null,
            null,
            null,
            null,
            true,
            null,
            "2026-06-24T00:00:00.0000000Z",
            "2026-06-24T00:00:00.0000000Z");

        return new SelectedPlaybackRule(id, name, rule, rule.Normalize());
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
        public FakeBookPlaybackContentService(PlaybackBookContent book)
        {
            Book = book;
        }

        public PlaybackBookContent Book { get; set; }

        public Dictionary<int, int> GetChapterCallCounts { get; } = [];

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            if (bookId != Book.BookId)
            {
                return Task.FromResult<PlaybackBookContent?>(null);
            }

            var metadataOnly = new PlaybackBookContent(
                Book.BookId,
                Book.BookTitle,
                Book.Chapters
                    .Select(chapter => PlaybackChapterContent.Unloaded(chapter.ChapterIndex, chapter.Title))
                    .ToArray(),
                Book.BookAuthor);
            return Task.FromResult<PlaybackBookContent?>(metadataOnly);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            GetChapterCallCounts[chapterIndex] = GetChapterCallCounts.GetValueOrDefault(chapterIndex) + 1;
            if (bookId != Book.BookId)
            {
                return Task.FromResult<PlaybackChapterContent?>(null);
            }

            return Task.FromResult<PlaybackChapterContent?>(Book.Chapters.FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex));
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
        private readonly Queue<Func<Task<PlaybackAudioResult>>> _results = [];

        public List<PlaybackAudioRequest> Requests { get; } = [];

        public int InvalidateCallCount { get; private set; }

        public void EnqueueFailure(TtsErrorKind kind, string message)
        {
            _results.Enqueue(() => Task.FromResult(new PlaybackAudioResult(
                null,
                false,
                new TtsExecutionFailure(kind, message, null, null, null, null))));
        }

        public void EnqueueSuccess(string filePath)
        {
            _results.Enqueue(() => Task.FromResult(new PlaybackAudioResult(filePath, false, null)));
        }

        public void EnqueueCachedSuccess(string filePath)
        {
            _results.Enqueue(() => Task.FromResult(new PlaybackAudioResult(filePath, true, null)));
        }

        public PendingAudioResult EnqueuePendingSuccess(string filePath)
        {
            var completionSource = new TaskCompletionSource<PlaybackAudioResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _results.Enqueue(() => completionSource.Task);
            return new PendingAudioResult(completionSource, filePath);
        }

        public Task<PlaybackAudioResult> GetAudioAsync(
            PlaybackAudioRequest request,
            PlaybackAudioPriority priority,
            Action<PlaybackAudioProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_results.Count > 0)
            {
                return _results.Dequeue().Invoke();
            }

            return Task.FromResult(new PlaybackAudioResult($"audio-{Requests.Count}.mp3", false, null));
        }

        public Task InvalidateAsync(PlaybackAudioRequest request, CancellationToken cancellationToken)
        {
            InvalidateCallCount++;
            return Task.CompletedTask;
        }

        public sealed class PendingAudioResult
        {
            private readonly TaskCompletionSource<PlaybackAudioResult> _completionSource;
            private readonly string _filePath;

            public PendingAudioResult(TaskCompletionSource<PlaybackAudioResult> completionSource, string filePath)
            {
                _completionSource = completionSource;
                _filePath = filePath;
            }

            public void CompleteSuccess()
            {
                _completionSource.TrySetResult(new PlaybackAudioResult(_filePath, false, null));
            }
        }
    }

    private sealed class FakeLocalAudioPlaybackCoordinator : ILocalAudioPlaybackCoordinator
    {
        public LocalAudioPlaybackSnapshot CurrentSnapshot { get; private set; } = LocalAudioPlaybackSnapshot.Idle;

        public LocalAudioPlaybackRequest? LastStartedRequest { get; private set; }

        public int StopCallCount { get; private set; }

        public bool WasDisposed { get; private set; }

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
            StopCallCount++;
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
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }

        public void RaiseCompleted()
        {
            CurrentSnapshot = CurrentSnapshot with
            {
                State = PlaybackState.Stopped,
                Message = "当前音频已播放完成。"
            };
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }

        public bool TryRaiseCompleted()
        {
            if (CurrentSnapshot.State != PlaybackState.Playing)
            {
                return false;
            }

            RaiseCompleted();
            return true;
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

        public ReadingProgressEntry? StoredProgress { get; set; }

        public Exception? SaveFailure { get; set; }

        public TaskCompletionSource<object?> SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?>? SaveGate { get; set; }

        public Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
        {
            SaveStarted.TrySetResult(null);
            if (SaveFailure is not null)
            {
                return Task.FromException(SaveFailure);
            }

            return SaveCoreAsync(progress, cancellationToken);
        }

        private async Task SaveCoreAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
        {
            if (SaveGate is not null)
            {
                await SaveGate.Task.WaitAsync(cancellationToken);
            }

            SavedProgress.Add(progress);
            StoredProgress = new ReadingProgressEntry(
                progress.BookId,
                progress.ChapterIndex,
                progress.SegmentIndex,
                progress.CharacterOffset,
                progress.AudioPositionMilliseconds,
                DateTimeOffset.UtcNow);
        }

        public Task<ReadingProgressEntry?> GetAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                StoredProgress is not null && string.Equals(StoredProgress.BookId, bookId, StringComparison.Ordinal)
                    ? StoredProgress
                    : null);
        }

        public Task<ReadingProgressEntry?> GetMostRecentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(StoredProgress);
        }
    }

    private sealed class FakePrefetchScheduler : IPlaybackPrefetchController
    {
        public List<(Guid SessionId, IReadOnlyList<PlaybackAudioRequest> Requests)> ScheduleCalls { get; } = [];

        public List<Guid> CancelledSessions { get; } = [];

        public Task SubmitAsync(PlaybackPrefetchWindow window, CancellationToken cancellationToken)
        {
            ScheduleCalls.Add((window.SessionId, window.Requests.ToArray()));
            return Task.CompletedTask;
        }

        public Task CancelAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            CancelledSessions.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAppSettingsStore : IAppSettingsService
    {
        public FakeAppSettingsStore(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }
        public AppSettings Current => Settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }
        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(Settings);
    }
}
