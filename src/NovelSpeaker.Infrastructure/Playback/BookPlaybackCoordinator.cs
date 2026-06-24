using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Coordinates book-oriented playback sessions on top of the low-level local audio pipeline.
/// </summary>
public sealed class PlaybackCoordinator : IPlaybackCoordinator
{
    private const int DefaultSpeakSpeed = 10;
    private const int FailurePauseThreshold = 2;

    private readonly IBookPlaybackContentService _bookContentService;
    private readonly ISelectedTtsRuleProvider _selectedRuleProvider;
    private readonly IPlaybackAudioProvider _audioProvider;
    private readonly ILocalAudioPlaybackCoordinator _localAudioPlaybackCoordinator;
    private readonly IReadingProgressStore _readingProgressStore;
    private readonly IPrefetchScheduler _prefetchScheduler;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private PlaybackSnapshot _currentSnapshot = PlaybackSnapshot.Idle;
    private PlaybackBookContent? _currentBook;
    private PlaybackSession? _currentSession;
    private SelectedPlaybackRule? _currentRule;
    private TtsErrorKind? _lastFailureKind;
    private string? _lastRecoveredCorruptSegmentKey;
    private bool _disposed;

    public PlaybackCoordinator(
        IBookPlaybackContentService bookContentService,
        ISelectedTtsRuleProvider selectedRuleProvider,
        IPlaybackAudioProvider audioProvider,
        ILocalAudioPlaybackCoordinator localAudioPlaybackCoordinator,
        IReadingProgressStore readingProgressStore,
        IPrefetchScheduler prefetchScheduler)
    {
        _bookContentService = bookContentService;
        _selectedRuleProvider = selectedRuleProvider;
        _audioProvider = audioProvider;
        _localAudioPlaybackCoordinator = localAudioPlaybackCoordinator;
        _readingProgressStore = readingProgressStore;
        _prefetchScheduler = prefetchScheduler;

        _localAudioPlaybackCoordinator.SnapshotChanged += OnLocalSnapshotChanged;
        _localAudioPlaybackCoordinator.PlaybackCompleted += OnLocalPlaybackCompleted;
        _localAudioPlaybackCoordinator.PlaybackFailed += OnLocalPlaybackFailed;
    }

    public PlaybackSnapshot CurrentSnapshot => _currentSnapshot;

    public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunSerializedAsync(ct => StartCoreAsync(request, ct), cancellationToken);
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(PauseCoreAsync, cancellationToken);
    }

    public Task ResumeAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ResumeCoreAsync, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(StopCoreAsync, cancellationToken);
    }

    public Task NextSegmentAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => MoveSegmentCoreAsync(1, ct), cancellationToken);
    }

    public Task PreviousSegmentAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => MoveSegmentCoreAsync(-1, ct), cancellationToken);
    }

    public Task NextChapterAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => MoveChapterCoreAsync(1, ct), cancellationToken);
    }

    public Task PreviousChapterAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => MoveChapterCoreAsync(-1, ct), cancellationToken);
    }

    public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(RetryCurrentSegmentCoreAsync, cancellationToken);
    }

    public Task SkipCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(SkipCurrentSegmentCoreAsync, cancellationToken);
    }

    public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => ChangeRuleCoreAsync(ruleId, ct), cancellationToken);
    }

    public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => ChangeSpeedCoreAsync(speakSpeed, ct), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localAudioPlaybackCoordinator.SnapshotChanged -= OnLocalSnapshotChanged;
        _localAudioPlaybackCoordinator.PlaybackCompleted -= OnLocalPlaybackCompleted;
        _localAudioPlaybackCoordinator.PlaybackFailed -= OnLocalPlaybackFailed;
        await DisposeSessionAsync();
        await _localAudioPlaybackCoordinator.DisposeAsync();
    }

    private async Task StartCoreAsync(PlaybackStartRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var book = await _bookContentService.GetBookAsync(request.BookId, cancellationToken);
        if (book is null)
        {
            PublishSnapshot(PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Faulted,
                Message = "未找到要播放的书籍。",
                CanRetry = false,
                CanSkip = false
            });
            return;
        }

        var startPosition = ResolveStartPosition(book, request.ChapterIndex, request.SegmentIndex);
        if (startPosition is null)
        {
            PublishSnapshot(new PlaybackSnapshot(
                PlaybackState.Faulted,
                book.BookId,
                book.BookTitle,
                0,
                null,
                0,
                0,
                null,
                null,
                request.SpeakSpeedOverride ?? _currentSnapshot.SpeakSpeed,
                0,
                0,
                "这本书没有可播放的文本分段。",
                false,
                false,
                false));
            return;
        }

        _currentBook = book;
        var selectedRule = await _selectedRuleProvider.GetSelectedRuleAsync(cancellationToken);
        if (selectedRule is null)
        {
            PublishSnapshot(CreateRuleMissingSnapshot(book, startPosition.Value.ChapterIndex, startPosition.Value.Chapter.Title, startPosition.Value.SegmentIndex));
            return;
        }

        var speakSpeed = NormalizeSpeakSpeed(request.SpeakSpeedOverride ?? _currentSnapshot.SpeakSpeed);
        await StartNewSessionAsync(
            book,
            startPosition.Value.ChapterIndex,
            startPosition.Value.SegmentIndex,
            request.ResumePositionMilliseconds ?? 0,
            selectedRule,
            speakSpeed,
            forceInvalidate: false,
            cancellationToken);
    }

    private async Task PauseCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentSession is null)
        {
            return;
        }

        await _localAudioPlaybackCoordinator.PauseAsync(cancellationToken);
        if (_currentBook is not null && _currentRule is not null)
        {
            var local = _localAudioPlaybackCoordinator.CurrentSnapshot;
            PublishSnapshot(BuildSnapshot(
                PlaybackState.Paused,
                _currentBook,
                _currentSession.ChapterIndex,
                _currentSession.SegmentIndex,
                _currentRule,
                _currentSession.SpeakSpeed,
                local.PositionMilliseconds,
                local.DurationMilliseconds,
                local.Message,
                local.IsUsingCache,
                false,
                false));
        }
        await SaveProgressAsync(_currentSession, _localAudioPlaybackCoordinator.CurrentSnapshot.PositionMilliseconds, cancellationToken);
    }

    private async Task ResumeCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentSession is null)
        {
            return;
        }

        await _localAudioPlaybackCoordinator.ResumeAsync(cancellationToken);
        if (_currentBook is not null && _currentRule is not null)
        {
            var local = _localAudioPlaybackCoordinator.CurrentSnapshot;
            PublishSnapshot(BuildSnapshot(
                PlaybackState.Playing,
                _currentBook,
                _currentSession.ChapterIndex,
                _currentSession.SegmentIndex,
                _currentRule,
                _currentSession.SpeakSpeed,
                local.PositionMilliseconds,
                local.DurationMilliseconds,
                local.Message,
                local.IsUsingCache,
                false,
                false));
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentSession is null)
        {
            PublishSnapshot(PlaybackSnapshot.Idle);
            return;
        }

        var session = _currentSession;
        await _localAudioPlaybackCoordinator.StopAsync(cancellationToken);
        await SaveProgressAsync(session, _localAudioPlaybackCoordinator.CurrentSnapshot.PositionMilliseconds, cancellationToken);
        await _prefetchScheduler.CancelAsync(session.SessionId, cancellationToken);
        await DisposeSessionAsync();

        PublishSnapshot(_currentSnapshot with
        {
            State = PlaybackState.Stopped,
            PositionMilliseconds = 0,
            DurationMilliseconds = _localAudioPlaybackCoordinator.CurrentSnapshot.DurationMilliseconds,
            Message = "已停止当前播放。",
            CanRetry = false,
            CanSkip = false
        });
    }

    private async Task MoveSegmentCoreAsync(int delta, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null || _currentRule is null)
        {
            return;
        }

        var currentPosition = GetCurrentPosition();
        var target = FindRelativeSegment(_currentBook, currentPosition.ChapterIndex, currentPosition.SegmentIndex, delta);
        if (target is null)
        {
            return;
        }

        await StartNewSessionAsync(
            _currentBook,
            target.Value.ChapterIndex,
            target.Value.SegmentIndex,
            0,
            _currentRule,
            GetCurrentSpeakSpeed(),
            forceInvalidate: false,
            cancellationToken);
    }

    private async Task MoveChapterCoreAsync(int delta, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null || _currentRule is null)
        {
            return;
        }

        var currentPosition = GetCurrentPosition();
        var target = FindRelativeChapter(_currentBook, currentPosition.ChapterIndex, delta);
        if (target is null)
        {
            return;
        }

        await StartNewSessionAsync(
            _currentBook,
            target.Value.ChapterIndex,
            0,
            0,
            _currentRule,
            GetCurrentSpeakSpeed(),
            forceInvalidate: false,
            cancellationToken);
    }

    private async Task RetryCurrentSegmentCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null)
        {
            return;
        }

        var rule = _currentRule ?? await _selectedRuleProvider.GetSelectedRuleAsync(cancellationToken);
        if (rule is null)
        {
            var current = GetCurrentPosition();
            PublishSnapshot(CreateRuleMissingSnapshot(_currentBook, current.ChapterIndex, GetChapterTitle(_currentBook, current.ChapterIndex), current.SegmentIndex));
            return;
        }

        var currentPosition = GetCurrentPosition();
        await StartNewSessionAsync(
            _currentBook,
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            0,
            rule,
            GetCurrentSpeakSpeed(),
            forceInvalidate: _lastFailureKind == TtsErrorKind.AudioDecode,
            cancellationToken);
    }

    private async Task SkipCurrentSegmentCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null || _currentRule is null)
        {
            return;
        }

        var currentPosition = GetCurrentPosition();
        var next = FindRelativeSegment(_currentBook, currentPosition.ChapterIndex, currentPosition.SegmentIndex, 1);
        if (next is null)
        {
            await StopCoreAsync(cancellationToken);
            PublishSnapshot(_currentSnapshot with
            {
                Message = "已跳过最后一段，播放结束。"
            });
            return;
        }

        await StartNewSessionAsync(
            _currentBook,
            next.Value.ChapterIndex,
            next.Value.SegmentIndex,
            0,
            _currentRule,
            GetCurrentSpeakSpeed(),
            forceInvalidate: false,
            cancellationToken);
    }

    private async Task ChangeRuleCoreAsync(long ruleId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var rule = await _selectedRuleProvider.SelectRuleAsync(ruleId, cancellationToken);
        if (rule is null)
        {
            if (_currentBook is not null)
            {
                var current = GetCurrentPosition();
                PublishSnapshot(CreateRuleMissingSnapshot(_currentBook, current.ChapterIndex, GetChapterTitle(_currentBook, current.ChapterIndex), current.SegmentIndex));
            }

            return;
        }

        _currentRule = rule;
        if (_currentBook is null)
        {
            PublishSnapshot(_currentSnapshot with
            {
                RuleId = rule.RuleId,
                RuleName = rule.RuleName,
                Message = $"已切换为规则：{rule.RuleName}"
            });
            return;
        }

        var currentPosition = GetCurrentPosition();
        await StartNewSessionAsync(
            _currentBook,
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            0,
            rule,
            GetCurrentSpeakSpeed(),
            forceInvalidate: false,
            cancellationToken);
    }

    private async Task ChangeSpeedCoreAsync(int speakSpeed, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var normalizedSpeed = NormalizeSpeakSpeed(speakSpeed);
        if (_currentBook is null || _currentRule is null)
        {
            PublishSnapshot(_currentSnapshot with
            {
                SpeakSpeed = normalizedSpeed,
                Message = $"语速已调整为 {normalizedSpeed}。"
            });
            return;
        }

        var currentPosition = GetCurrentPosition();
        await StartNewSessionAsync(
            _currentBook,
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            0,
            _currentRule,
            normalizedSpeed,
            forceInvalidate: false,
            cancellationToken);
    }

    private async Task StartNewSessionAsync(
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex,
        long resumePositionMilliseconds,
        SelectedPlaybackRule selectedRule,
        int speakSpeed,
        bool forceInvalidate,
        CancellationToken cancellationToken)
    {
        await _prefetchScheduler.CancelAsync(_currentSession?.SessionId ?? Guid.Empty, cancellationToken);
        await DisposeSessionAsync();

        var session = new PlaybackSession(
            book.BookId,
            chapterIndex,
            segmentIndex,
            selectedRule.RuleId,
            selectedRule.RuleName,
            speakSpeed);

        _currentBook = book;
        _currentRule = selectedRule;
        _currentSession = session;
        _lastFailureKind = null;
        _lastRecoveredCorruptSegmentKey = null;

        PublishSnapshot(BuildSnapshot(
            PlaybackState.Preparing,
            book,
            chapterIndex,
            segmentIndex,
            selectedRule,
            speakSpeed,
            0,
            0,
            "正在准备当前段落。",
            false,
            false,
            false));

        await PlayCurrentSegmentAsync(session, resumePositionMilliseconds, forceInvalidate, cancellationToken);
    }

    private async Task PlayCurrentSegmentAsync(
        PlaybackSession session,
        long resumePositionMilliseconds,
        bool forceInvalidate,
        CancellationToken cancellationToken)
    {
        if (_currentBook is null || _currentRule is null)
        {
            return;
        }

        var chapter = GetChapter(_currentBook, session.ChapterIndex);
        if (chapter is null)
        {
            PublishPlaybackFailure("未找到要播放的章节。", TtsErrorKind.InvalidRule, canSkip: false);
            return;
        }

        if (session.SegmentIndex < 0 || session.SegmentIndex >= chapter.Segments.Count)
        {
            PublishPlaybackFailure("未找到要播放的段落。", TtsErrorKind.InvalidRule, canSkip: false);
            return;
        }

        var segment = chapter.Segments[session.SegmentIndex];
        var audioRequest = new PlaybackAudioRequest(
            _currentBook.BookId,
            chapter.ChapterIndex,
            session.SegmentIndex,
            segment.SpeechText,
            _currentRule.RuleId,
            _currentRule.SourceRule,
            _currentRule.NormalizedRule,
            session.SpeakSpeed,
            session.SessionId);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.CancellationToken);

        PublishSnapshot(BuildSnapshot(
            PlaybackState.Buffering,
            _currentBook,
            chapter.ChapterIndex,
            session.SegmentIndex,
            _currentRule,
            session.SpeakSpeed,
            0,
            0,
            forceInvalidate ? "正在重新生成当前段音频。" : "正在加载当前段音频。",
            false,
            false,
            false));

        if (forceInvalidate)
        {
            PublishSnapshot(_currentSnapshot with { State = PlaybackState.Recovering, Message = "检测到音频损坏，正在重新生成。" });
            await _audioProvider.InvalidateAsync(audioRequest, linkedCts.Token);
        }

        var audio = await _audioProvider.GetAudioAsync(audioRequest, linkedCts.Token);
        if (!IsSessionCurrent(session.SessionId))
        {
            return;
        }

        if (!audio.IsSuccess)
        {
            HandleSegmentFailure(audio.Failure!, canSkip: HasNextSegment(_currentBook, chapter.ChapterIndex, session.SegmentIndex));
            return;
        }

        session.ConsecutiveSegmentFailureCount = 0;
        _lastFailureKind = null;
        _lastRecoveredCorruptSegmentKey = null;

        await _localAudioPlaybackCoordinator.StartAsync(
            new LocalAudioPlaybackRequest(
                audio.FilePath!,
                $"{_currentBook.BookTitle} · {chapter.Title}",
                _currentBook.BookId,
                chapter.ChapterIndex,
                session.SegmentIndex,
                resumePositionMilliseconds,
                audio.IsUsingCache),
            linkedCts.Token);

        if (!IsSessionCurrent(session.SessionId))
        {
            return;
        }

        var local = _localAudioPlaybackCoordinator.CurrentSnapshot;
        PublishSnapshot(BuildSnapshot(
            local.State,
            _currentBook,
            chapter.ChapterIndex,
            session.SegmentIndex,
            _currentRule,
            session.SpeakSpeed,
            local.PositionMilliseconds,
            local.DurationMilliseconds,
            local.Message,
            local.IsUsingCache,
            false,
            false));

        await SchedulePrefetchAsync(session, linkedCts.Token);
    }

    private void HandleSegmentFailure(TtsExecutionFailure failure, bool canSkip)
    {
        _lastFailureKind = failure.Kind;
        if (_currentSession is null)
        {
            PublishPlaybackFailure(failure.Message, failure.Kind, canSkip);
            return;
        }

        _currentSession.ConsecutiveSegmentFailureCount++;
        PublishSnapshot(_currentSnapshot with
        {
            State = PlaybackState.Faulted,
            Message = failure.Message,
            CanRetry = true,
            CanSkip = canSkip
        });

        if (_currentSession.ConsecutiveSegmentFailureCount >= FailurePauseThreshold)
        {
            PublishSnapshot(_currentSnapshot with
            {
                State = PlaybackState.Faulted,
                Message = $"已连续 {_currentSession.ConsecutiveSegmentFailureCount} 段播放失败，请重试、跳过或停止。",
                CanRetry = true,
                CanSkip = canSkip
            });
        }
    }

    private void PublishPlaybackFailure(string message, TtsErrorKind failureKind, bool canSkip)
    {
        _lastFailureKind = failureKind;
        PublishSnapshot(_currentSnapshot with
        {
            State = PlaybackState.Faulted,
            Message = message,
            CanRetry = true,
            CanSkip = canSkip
        });
    }

    private async Task SchedulePrefetchAsync(PlaybackSession session, CancellationToken cancellationToken)
    {
        if (_currentBook is null || _currentRule is null)
        {
            return;
        }

        var requests = new List<PlaybackAudioRequest>();
        var next = FindRelativeSegment(_currentBook, session.ChapterIndex, session.SegmentIndex, 1);
        if (next is not null)
        {
            AddPrefetchRequest(requests, session, next.Value.ChapterIndex, next.Value.SegmentIndex);
        }

        var afterNext = next is null
            ? null
            : FindRelativeSegment(_currentBook, next.Value.ChapterIndex, next.Value.SegmentIndex, 1);
        if (afterNext is not null)
        {
            AddPrefetchRequest(requests, session, afterNext.Value.ChapterIndex, afterNext.Value.SegmentIndex);
        }

        await _prefetchScheduler.ScheduleAsync(session.SessionId, requests, cancellationToken);
    }

    private void AddPrefetchRequest(List<PlaybackAudioRequest> requests, PlaybackSession session, int chapterIndex, int segmentIndex)
    {
        if (_currentBook is null || _currentRule is null)
        {
            return;
        }

        var chapter = GetChapter(_currentBook, chapterIndex);
        if (chapter is null || segmentIndex < 0 || segmentIndex >= chapter.Segments.Count)
        {
            return;
        }

        requests.Add(new PlaybackAudioRequest(
            _currentBook.BookId,
            chapterIndex,
            segmentIndex,
            chapter.Segments[segmentIndex].SpeechText,
            _currentRule.RuleId,
            _currentRule.SourceRule,
            _currentRule.NormalizedRule,
            session.SpeakSpeed,
            session.SessionId));
    }

    private async void OnLocalPlaybackCompleted(object? sender, EventArgs e)
    {
        await RunSerializedWithoutUserCancellationAsync(async () =>
        {
            if (_currentSession is null || _currentBook is null)
            {
                return;
            }

            var next = FindRelativeSegment(_currentBook, _currentSession.ChapterIndex, _currentSession.SegmentIndex, 1);
            if (next is null)
            {
                await SaveProgressAsync(_currentSession, _localAudioPlaybackCoordinator.CurrentSnapshot.DurationMilliseconds, CancellationToken.None);
                await _prefetchScheduler.CancelAsync(_currentSession.SessionId, CancellationToken.None);
                await DisposeSessionAsync();

                PublishSnapshot(_currentSnapshot with
                {
                    State = PlaybackState.Stopped,
                    PositionMilliseconds = 0,
                    Message = "全书播放完成。",
                    CanRetry = false,
                    CanSkip = false
                });
                return;
            }

            await SaveProgressAsync(
                _currentSession,
                _localAudioPlaybackCoordinator.CurrentSnapshot.DurationMilliseconds,
                CancellationToken.None);

            _currentSession.ChapterIndex = next.Value.ChapterIndex;
            _currentSession.SegmentIndex = next.Value.SegmentIndex;
            await PlayCurrentSegmentAsync(_currentSession, 0, forceInvalidate: false, CancellationToken.None);
        });
    }

    private async void OnLocalPlaybackFailed(object? sender, PlaybackErrorEventArgs error)
    {
        await RunSerializedWithoutUserCancellationAsync(async () =>
        {
            if (_currentSession is null || _currentBook is null)
            {
                PublishPlaybackFailure(error.Message, TtsErrorKind.AudioDecode, canSkip: false);
                return;
            }

            if (error.Kind == PlaybackErrorKind.AudioDecode)
            {
                var recoveryKey = $"{_currentSession.SessionId:N}:{_currentSession.ChapterIndex}:{_currentSession.SegmentIndex}:{_currentSession.RuleId}:{_currentSession.SpeakSpeed}";
                if (!string.Equals(_lastRecoveredCorruptSegmentKey, recoveryKey, StringComparison.Ordinal))
                {
                    _lastRecoveredCorruptSegmentKey = recoveryKey;
                    await PlayCurrentSegmentAsync(_currentSession, 0, forceInvalidate: true, CancellationToken.None);
                    return;
                }
            }

            PublishPlaybackFailure(error.Message, TtsErrorKind.AudioDecode, HasNextSegment(_currentBook, _currentSession.ChapterIndex, _currentSession.SegmentIndex));
        });
    }

    private async void OnLocalSnapshotChanged(object? sender, LocalAudioPlaybackSnapshot snapshot)
    {
        await RunSerializedWithoutUserCancellationAsync(() =>
        {
            if (_currentSession is null || _currentBook is null || _currentRule is null)
            {
                return Task.CompletedTask;
            }

            if (snapshot.State is PlaybackState.Stopped or PlaybackState.Faulted)
            {
                return Task.CompletedTask;
            }

            PublishSnapshot(BuildSnapshot(
                snapshot.State,
                _currentBook,
                _currentSession.ChapterIndex,
                _currentSession.SegmentIndex,
                _currentRule,
                _currentSession.SpeakSpeed,
                snapshot.PositionMilliseconds,
                snapshot.DurationMilliseconds,
                snapshot.Message,
                snapshot.IsUsingCache,
                false,
                false));
            return Task.CompletedTask;
        });
    }

    private static PlaybackSnapshot CreateRuleMissingSnapshot(
        PlaybackBookContent book,
        int chapterIndex,
        string? chapterTitle,
        int segmentIndex)
    {
        return new PlaybackSnapshot(
            PlaybackState.Faulted,
            book.BookId,
            book.BookTitle,
            chapterIndex,
            chapterTitle,
            segmentIndex,
            0,
            null,
            null,
            DefaultSpeakSpeed,
            0,
            0,
            "当前没有可用的 TTS 规则，请先前往规则页选择或导入规则。",
            false,
            false,
            false);
    }

    private PlaybackSnapshot BuildSnapshot(
        PlaybackState state,
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex,
        SelectedPlaybackRule selectedRule,
        int speakSpeed,
        long positionMilliseconds,
        long durationMilliseconds,
        string? message,
        bool isUsingCache,
        bool canRetry,
        bool canSkip)
    {
        var chapter = GetChapter(book, chapterIndex);
        return new PlaybackSnapshot(
            state,
            book.BookId,
            book.BookTitle,
            chapterIndex,
            chapter?.Title,
            segmentIndex,
            chapter?.Segments.Count ?? 0,
            selectedRule.RuleId,
            selectedRule.RuleName,
            speakSpeed,
            positionMilliseconds,
            durationMilliseconds,
            message,
            isUsingCache,
            canRetry,
            canSkip);
    }

    private (int ChapterIndex, int SegmentIndex) GetCurrentPosition()
    {
        if (_currentSession is not null)
        {
            return (_currentSession.ChapterIndex, _currentSession.SegmentIndex);
        }

        return (_currentSnapshot.ChapterIndex, _currentSnapshot.SegmentIndex);
    }

    private int GetCurrentSpeakSpeed()
    {
        return NormalizeSpeakSpeed(_currentSession?.SpeakSpeed ?? _currentSnapshot.SpeakSpeed);
    }

    private static int NormalizeSpeakSpeed(int speakSpeed)
    {
        return speakSpeed <= 0 ? DefaultSpeakSpeed : speakSpeed;
    }

    private static PlaybackChapterContent? GetChapter(PlaybackBookContent book, int chapterIndex)
    {
        return book.Chapters.FirstOrDefault(chapter => chapter.ChapterIndex == chapterIndex);
    }

    private static string? GetChapterTitle(PlaybackBookContent book, int chapterIndex)
    {
        return GetChapter(book, chapterIndex)?.Title;
    }

    private static bool HasNextSegment(PlaybackBookContent book, int chapterIndex, int segmentIndex)
    {
        return FindRelativeSegment(book, chapterIndex, segmentIndex, 1) is not null;
    }

    private static (PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex)? ResolveStartPosition(
        PlaybackBookContent book,
        int? preferredChapterIndex,
        int? preferredSegmentIndex)
    {
        var orderedChapters = book.Chapters.OrderBy(chapter => chapter.ChapterIndex).ToArray();
        foreach (var chapter in orderedChapters)
        {
            if (chapter.Segments.Count == 0)
            {
                continue;
            }

            if (preferredChapterIndex is null)
            {
                return (chapter, chapter.ChapterIndex, 0);
            }

            if (chapter.ChapterIndex < preferredChapterIndex.Value)
            {
                continue;
            }

            if (chapter.ChapterIndex == preferredChapterIndex.Value)
            {
                var segmentIndex = preferredSegmentIndex is >= 0 and < int.MaxValue
                    ? Math.Min(preferredSegmentIndex.Value, chapter.Segments.Count - 1)
                    : 0;
                return (chapter, chapter.ChapterIndex, segmentIndex);
            }

            return (chapter, chapter.ChapterIndex, 0);
        }

        return null;
    }

    private static (int ChapterIndex, int SegmentIndex)? FindRelativeSegment(
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex,
        int delta)
    {
        if (delta == 0)
        {
            return (chapterIndex, segmentIndex);
        }

        var flatSegments = book.Chapters
            .OrderBy(chapter => chapter.ChapterIndex)
            .SelectMany(chapter => chapter.Segments.Select(segment => (chapter.ChapterIndex, segment.SegmentIndex)))
            .ToArray();

        var currentIndex = Array.FindIndex(flatSegments, item => item.ChapterIndex == chapterIndex && item.SegmentIndex == segmentIndex);
        if (currentIndex < 0)
        {
            return flatSegments.Length == 0 ? null : flatSegments[0];
        }

        var targetIndex = currentIndex + delta;
        return targetIndex < 0 || targetIndex >= flatSegments.Length
            ? null
            : flatSegments[targetIndex];
    }

    private static (int ChapterIndex, int SegmentIndex)? FindRelativeChapter(
        PlaybackBookContent book,
        int chapterIndex,
        int delta)
    {
        var chapters = book.Chapters
            .Where(chapter => chapter.Segments.Count > 0)
            .OrderBy(chapter => chapter.ChapterIndex)
            .ToArray();

        var currentIndex = Array.FindIndex(chapters, chapter => chapter.ChapterIndex == chapterIndex);
        if (currentIndex < 0)
        {
            return chapters.Length == 0 ? null : (chapters[0].ChapterIndex, 0);
        }

        var targetIndex = currentIndex + delta;
        return targetIndex < 0 || targetIndex >= chapters.Length
            ? null
            : (chapters[targetIndex].ChapterIndex, 0);
    }

    private bool IsSessionCurrent(Guid sessionId)
    {
        return _currentSession is not null && _currentSession.SessionId == sessionId;
    }

    private async Task SaveProgressAsync(PlaybackSession session, long positionMilliseconds, CancellationToken cancellationToken)
    {
        await _readingProgressStore.SaveAsync(
            new PlaybackProgressUpdate(
                session.BookId,
                session.ChapterIndex,
                session.SegmentIndex,
                positionMilliseconds),
            cancellationToken);
    }

    private async Task DisposeSessionAsync()
    {
        if (_currentSession is null)
        {
            return;
        }

        _currentSession.Cancel();
        await _currentSession.DisposeAsync();
        _currentSession = null;
    }

    private async Task RunSerializedAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await action(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task RunSerializedWithoutUserCancellationAsync(Func<Task> action)
    {
        if (_disposed)
        {
            return;
        }

        var entered = false;
        try
        {
            await _mutex.WaitAsync();
            entered = true;

            if (_disposed)
            {
                return;
            }

            await action();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        finally
        {
            if (entered)
            {
                try
                {
                    _mutex.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private void PublishSnapshot(PlaybackSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
