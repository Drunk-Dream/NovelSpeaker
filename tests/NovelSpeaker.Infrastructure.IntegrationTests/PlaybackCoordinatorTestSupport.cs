using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Cache;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed partial class PlaybackCoordinatorTests
{
    private static PlaybackCoordinator CreateCoordinator(
        FakeLocalAudioPlaybackCoordinator localCoordinator,
        IBookPlaybackContentService? bookContentService = null,
        FakeCurrentSpeechProvider? selectedProviderProvider = null,
        FakeAudioGenerationProvider? audioProvider = null,
        PlaybackBookContent? book = null,
        FakeReadingProgressStore? readingProgressStore = null,
        FakePrefetchScheduler? prefetchScheduler = null,
        FakeAppSettingsStore? appSettingsStore = null,
        TimeProvider? timeProvider = null,
        IBookSourceChangeSource? sourceChanges = null,
        IRegexReplacementRuleWorkspaceService? regexWorkspace = null,
        IAudioCacheProtectionRegistry? protectionRegistry = null)
    {
        return new PlaybackCoordinator(
            bookContentService ?? new FakeBookPlaybackContentService(book ?? CreateBook()),
            selectedProviderProvider ?? new FakeCurrentSpeechProvider(CreateRuleSelection(1, "默认规则")),
            new PlaybackSegmentRunner(
                audioProvider ?? new FakeAudioGenerationProvider(),
                localCoordinator),
            new PlaybackRecoveryPolicy(),
            protectionRegistry ?? new AudioCacheProtectionRegistry(),
            localCoordinator,
            new PlaybackProgressController(readingProgressStore ?? new FakeReadingProgressStore()),
            prefetchScheduler ?? new FakePrefetchScheduler(),
            appSettingsStore ?? new FakeAppSettingsStore(AppSettings.Default),
            timeProvider ?? TimeProvider.System,
            sourceChanges: sourceChanges,
            regexWorkspace: regexWorkspace);
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

    private static PlaybackBookContent CreateSegmentBook(int segmentCount)
    {
        return new PlaybackBookContent(
            "book-1",
            "示例小说",
            [PlaybackChapterContent.FromLoaded(
                0,
                "第一章 开始",
                Enumerable.Range(0, segmentCount)
                    .Select(index => new SpeechSegment(index, index * 6, 6, $"第 {index + 1} 段", $"第 {index + 1} 段"))
                    .ToArray())]);
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

    private static ResolvedSpeechProvider CreateRuleSelection(long id, string name)
    {
        var rule = TestSpeechProviders.Create(id, name, "https://example.com/tts?text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}");

        return TestSpeechProviders.Resolve(rule);
    }

    private static async Task WaitForAsync(
        PlaybackCoordinator coordinator,
        Func<bool> condition)
    {
        if (condition())
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<PlaybackSnapshot>? handler = null;
        handler = (_, _) =>
        {
            if (condition())
            {
                completion.TrySetResult();
            }
        };
        coordinator.SnapshotChanged += handler;
        try
        {
            if (condition())
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            coordinator.SnapshotChanged -= handler;
        }
    }

    private static async Task WaitForAsync(
        FakeAudioGenerationProvider audioProvider,
        Func<bool> condition)
    {
        if (condition())
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (condition())
            {
                completion.TrySetResult();
            }
        };
        audioProvider.ActivityChanged += handler;
        try
        {
            if (condition())
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            audioProvider.ActivityChanged -= handler;
        }
    }

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        public FakeBookPlaybackContentService(PlaybackBookContent book)
        {
            Book = book;
        }

        public PlaybackBookContent Book { get; set; }

        public TaskCompletionSource? CurrentContextGate { get; set; }

        public async Task<bool> IsCurrentAsync(PlaybackBookContent book, CancellationToken cancellationToken)
        {
            if (CurrentContextGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
            return book.BookId == Book.BookId && book.SourceContext == Book.SourceContext;
        }

        public Dictionary<int, int> GetChapterCallCounts { get; } = [];
        public Exception? ChapterFailure { get; set; }
        public TaskCompletionSource? ChapterGate { get; set; }
        public TaskCompletionSource ChapterRequested { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IgnoreChapterCancellation { get; set; }

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
                    .Select(chapter => PlaybackChapterContent.Unloaded(chapter.ChapterIndex, chapter.Title, chapter.ChapterId))
                    .ToArray(),
                Book.BookAuthor,
                Book.SourceContext);
            return Task.FromResult<PlaybackBookContent?>(metadataOnly);
        }

        public async Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            GetChapterCallCounts[chapterIndex] = GetChapterCallCounts.GetValueOrDefault(chapterIndex) + 1;
            ChapterRequested.TrySetResult();
            if (ChapterGate is { } gate)
                await gate.Task.WaitAsync(IgnoreChapterCancellation ? CancellationToken.None : cancellationToken);
            if (ChapterFailure is { } failure) throw failure;
            if (bookId != Book.BookId)
            {
                return null;
            }

            return Book.Chapters.FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex);
        }
    }

    private sealed class FakeCurrentSpeechProvider : TestCurrentSpeechProvider
    {
        private readonly Dictionary<ProviderId, ResolvedSpeechProvider> _rules = [];
        public override event EventHandler<SpeechProvidersChangedEventArgs>? Changed;

        public void CommitProvider(ResolvedSpeechProvider? provider)
        {
            SelectedProvider = provider;
            Changed?.Invoke(this, new(true));
        }

        public FakeCurrentSpeechProvider(ResolvedSpeechProvider? selectedProvider)
        {
            if (selectedProvider is not null)
            {
                SelectedProvider = selectedProvider;
                _rules[selectedProvider.ProviderId] = selectedProvider;
            }
        }

        public ResolvedSpeechProvider? SelectedProvider { get; private set; }

        public void RegisterSelectable(ResolvedSpeechProvider rule)
        {
            _rules[rule.ProviderId] = rule;
        }

        public override Task<ResolvedSpeechProvider?> GetSelectedProviderAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SelectedProvider);
        }

        public override Task<ResolvedSpeechProvider?> SelectProviderAsync(ProviderId ruleId, CancellationToken cancellationToken)
        {
            SelectedProvider = _rules.GetValueOrDefault(ruleId);
            return Task.FromResult(SelectedProvider);
        }
    }

    private sealed class FakeAudioGenerationProvider : IAudioGenerationProvider
    {
        private readonly Queue<Func<Task<AudioGenerationResult>>> _results = [];

        public List<AudioGenerationRequest> Requests { get; } = [];

        public event EventHandler? ActivityChanged;

        public int InvalidateCallCount { get; private set; }

        public void EnqueueFailure(TtsErrorKind kind, string message)
        {
            _results.Enqueue(() => Task.FromResult(new AudioGenerationResult(
                null,
                false,
                new TtsExecutionFailure(kind, message, null, null, null, null))));
        }

        public void EnqueueException(Exception exception)
        {
            _results.Enqueue(() => Task.FromException<AudioGenerationResult>(exception));
        }

        public void EnqueueSuccess(string filePath)
        {
            _results.Enqueue(() => Task.FromResult(new AudioGenerationResult(filePath, false, null)));
        }

        public void EnqueueCachedSuccess(string filePath)
        {
            _results.Enqueue(() => Task.FromResult(new AudioGenerationResult(filePath, true, null)));
        }

        public PendingAudioResult EnqueuePendingSuccess(string filePath)
        {
            var completionSource = new TaskCompletionSource<AudioGenerationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _results.Enqueue(() =>
            {
                started.TrySetResult(null);
                return completionSource.Task;
            });
            return new PendingAudioResult(completionSource, filePath, started.Task);
        }

        public bool ObserveCancellation { get; set; }

        public Task<AudioGenerationResult> GetAudioAsync(
            AudioGenerationRequest request,
            AudioGenerationPriority priority,
            Action<AudioGenerationProgress>? progressCallback,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            ActivityChanged?.Invoke(this, EventArgs.Empty);
            if (_results.Count > 0)
            {
                var result = _results.Dequeue().Invoke();
                return ObserveCancellation ? result.WaitAsync(cancellationToken) : result;
            }

            return Task.FromResult(new AudioGenerationResult($"audio-{Requests.Count}.mp3", false, null));
        }

        public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken)
        {
            InvalidateCallCount++;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public sealed class PendingAudioResult
        {
            private readonly TaskCompletionSource<AudioGenerationResult> _completionSource;
            private readonly string _filePath;

            public PendingAudioResult(
                TaskCompletionSource<AudioGenerationResult> completionSource,
                string filePath,
                Task started)
            {
                _completionSource = completionSource;
                _filePath = filePath;
                Started = started;
            }

            public Task Started { get; }

            public void CompleteSuccess()
            {
                _completionSource.TrySetResult(new AudioGenerationResult(_filePath, false, null));
            }
        }
    }

    private sealed class FakeLocalAudioPlaybackCoordinator : ILocalAudioPlaybackCoordinator
    {
        public LocalAudioPlaybackSnapshot CurrentSnapshot { get; private set; } = LocalAudioPlaybackSnapshot.Idle;

        private long _audioGeneration;

        public double Volume { get; private set; } = PlaybackVolume.Default;

        public LocalAudioPlaybackRequest? LastStartedRequest { get; private set; }

        public int StartCallCount { get; private set; }

        public int? CompleteOnStartCall { get; init; }

        public int PauseCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public bool WasDisposed { get; private set; }

        public event EventHandler<LocalAudioPlaybackSnapshot>? SnapshotChanged;

        public event EventHandler<LocalAudioPlaybackSnapshot>? PlaybackCompleted;

        public event EventHandler<LocalAudioPlaybackFailure>? PlaybackFailed;

        public Task StartAsync(LocalAudioPlaybackRequest request, CancellationToken cancellationToken)
        {
            StartCallCount++;
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
                request.IsUsingCache,
                PlaybackVolume.Default,
                request.PlaybackSessionId,
                ++_audioGeneration,
                request.TargetRevision,
                request.PreparationAttemptId);
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            if (StartCallCount == CompleteOnStartCall)
            {
                RaiseCompleted();
            }
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
            PauseCallCount++;
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

        public void SetVolume(double volume)
        {
            Volume = PlaybackVolume.Normalize(volume);
            CurrentSnapshot = CurrentSnapshot with { Volume = this.Volume };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
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
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            PlaybackCompleted?.Invoke(this, CurrentSnapshot);
        }

        public void RaiseHistoricalSnapshot(LocalAudioPlaybackSnapshot snapshot) =>
            SnapshotChanged?.Invoke(this, snapshot);

        public void RaiseHistoricalCompleted(LocalAudioPlaybackSnapshot snapshot) =>
            PlaybackCompleted?.Invoke(this, snapshot);

        public void RaiseHistoricalFailed(LocalAudioPlaybackSnapshot snapshot, PlaybackErrorKind kind, string message) =>
            PlaybackFailed?.Invoke(this, new(snapshot, new PlaybackErrorEventArgs(kind, message)));

        public void PublishSnapshot(LocalAudioPlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
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
            CurrentSnapshot = CurrentSnapshot with { State = PlaybackState.Faulted, Message = message };
            SnapshotChanged?.Invoke(this, CurrentSnapshot);
            PlaybackFailed?.Invoke(this, new(CurrentSnapshot, new PlaybackErrorEventArgs(kind, message)));
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

        public int? SaveFailureCall { get; set; }

        public int SaveCallCount { get; private set; }

        public TaskCompletionSource<object?> SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<int> SecondSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<int> ThirdSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?>? SaveGate { get; set; }

        public int? SaveGateCall { get; set; }

        public Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            SaveStarted.TrySetResult(null);
            if (SaveCallCount == 2)
            {
                SecondSaveStarted.TrySetResult(SaveCallCount);
            }
            else if (SaveCallCount == 3)
            {
                ThirdSaveStarted.TrySetResult(SaveCallCount);
            }
            if (SaveFailure is not null &&
                (SaveFailureCall is null || SaveFailureCall == SaveCallCount))
            {
                return Task.FromException(SaveFailure);
            }

            return SaveCoreAsync(progress, cancellationToken);
        }

        private async Task SaveCoreAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
        {
            if (SaveGate is not null &&
                (SaveGateCall is null || SaveGateCall == SaveCallCount))
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
        public List<(Guid SessionId, IReadOnlyList<AudioGenerationRequest> Requests)> ScheduleCalls { get; } = [];

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

        public List<AppSettingsUpdate> Updates { get; } = [];

        public TaskCompletionSource<AppSettingsUpdate> UpdateCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AppSettings Current => Settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public void ReplaceSnapshot(AppSettings settings)
        {
            var previous = Settings;
            Settings = settings.Normalize();
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, Settings, isSnapshotReplacement: true));
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            Updates.Add(update);
            var previous = Settings;
            Settings = (Settings with
            {
                PlaybackVolume = update.PlaybackVolume ?? Settings.PlaybackVolume,
                PrefetchCount = update.PrefetchCount ?? Settings.PrefetchCount
            }).Normalize();
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, Settings));
            UpdateCompleted.TrySetResult(update);
            return Task.FromResult(Settings);
        }
    }
}
