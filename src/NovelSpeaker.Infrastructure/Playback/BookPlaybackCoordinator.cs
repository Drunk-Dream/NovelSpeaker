using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Coordinates book-oriented playback sessions on top of the low-level local audio pipeline.
/// </summary>
public sealed class PlaybackCoordinator : IPlaybackCoordinator
{
    private const int FailurePauseThreshold = 2;

    private readonly IBookPlaybackContentService _bookContentService;
    private readonly ISelectedTtsRuleProvider _selectedRuleProvider;
    private readonly IPlaybackAudioProvider _audioProvider;
    private readonly IAudioCacheProtectionRegistry _audioCacheProtectionRegistry;
    private readonly ILocalAudioPlaybackCoordinator _localAudioPlaybackCoordinator;
    private readonly IReadingProgressStore _readingProgressStore;
    private readonly IPrefetchScheduler _prefetchScheduler;
    private readonly IAppSettingsService _appSettingsService;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private PlaybackSnapshot _currentSnapshot = PlaybackSnapshot.Idle;
    private PlaybackBookContent? _currentBook;
    private PlaybackSession? _currentSession;
    private SelectedPlaybackRule? _currentRule;
    private IDisposable? _currentAudioProtection;
    private TtsErrorKind? _lastFailureKind;
    private string? _lastRecoveredCorruptSegmentKey;
    private long _contentRevision;
    private bool _disposed;

    public PlaybackCoordinator(
        IBookPlaybackContentService bookContentService,
        ISelectedTtsRuleProvider selectedRuleProvider,
        IPlaybackAudioProvider audioProvider,
        IAudioCacheProtectionRegistry audioCacheProtectionRegistry,
        ILocalAudioPlaybackCoordinator localAudioPlaybackCoordinator,
        IReadingProgressStore readingProgressStore,
        IPrefetchScheduler prefetchScheduler,
        IAppSettingsService appSettingsService)
    {
        _bookContentService = bookContentService;
        _selectedRuleProvider = selectedRuleProvider;
        _audioProvider = audioProvider;
        _audioCacheProtectionRegistry = audioCacheProtectionRegistry;
        _localAudioPlaybackCoordinator = localAudioPlaybackCoordinator;
        _readingProgressStore = readingProgressStore;
        _prefetchScheduler = prefetchScheduler;
        _appSettingsService = appSettingsService;

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

    public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunSerializedAsync(ct => OpenPausedCoreAsync(request, ct), cancellationToken);
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

    public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return RunSerializedAsync(ct => JumpToCoreAsync(target.ChapterIndex, target.SegmentIndex, ct), cancellationToken);
    }

    public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => JumpToChapterCoreAsync(chapterIndex, ct), cancellationToken);
    }

    public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => JumpToCoreAsync(chapterIndex, segmentIndex, ct), cancellationToken);
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

    public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return RunSerializedAsync(ct => RefreshBookMetadataCoreAsync(bookId, ct), cancellationToken);
    }

    public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(RefreshRegexReplacementCoreAsync, cancellationToken);
    }

    public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return RunSerializedAsync(ct => HandleBookDeletedCoreAsync(bookId, ct), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            if (_currentSession is not null)
            {
                await SaveProgressAsync(
                    _currentSession,
                    GetCurrentPositionMillisecondsForSave(_currentSession),
                    CancellationToken.None).ConfigureAwait(false);
            }

            _disposed = true;
            _localAudioPlaybackCoordinator.SnapshotChanged -= OnLocalSnapshotChanged;
            _localAudioPlaybackCoordinator.PlaybackCompleted -= OnLocalPlaybackCompleted;
            _localAudioPlaybackCoordinator.PlaybackFailed -= OnLocalPlaybackFailed;
            await DisposeSessionAsync().ConfigureAwait(false);
            ClearProtectedPlaybackFile();
        }
        finally
        {
            _mutex.Release();
        }

        await _localAudioPlaybackCoordinator.DisposeAsync().ConfigureAwait(false);
    }

    private async Task StartCoreAsync(PlaybackStartRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var resolved = await ResolveBookStartContextAsync(
            request.BookId,
            request.ChapterIndex,
            request.SegmentIndex,
            request.ResumePositionMilliseconds,
            allowSavedProgress: true,
            cancellationToken).ConfigureAwait(false);
        if (resolved is null)
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

        var selectedRule = await _selectedRuleProvider.GetSelectedRuleAsync(cancellationToken).ConfigureAwait(false);
        var speakSpeed = NormalizeSpeakSpeed(request.SpeakSpeedOverride ?? _currentSnapshot.SpeakSpeed);

        if (selectedRule is null)
        {
            await OpenResolvedPositionAsync(
                resolved.Value.Book,
                resolved.Value.ChapterIndex,
                resolved.Value.SegmentIndex,
                resolved.Value.ResumePositionMilliseconds,
                selectedRule,
                speakSpeed,
                "当前没有可用的 TTS 规则，请先前往规则页选择或导入规则。",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await StartNewSessionAsync(
            resolved.Value.Book,
            resolved.Value.ChapterIndex,
            resolved.Value.SegmentIndex,
            resolved.Value.ResumePositionMilliseconds,
            selectedRule,
            speakSpeed,
            forceInvalidate: false,
            playImmediately: true,
            pausedState: PlaybackState.Paused,
            pausedMessage: "已恢复到当前位置，等待播放。",
            cancellationToken);
    }

    private async Task OpenPausedCoreAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var resolved = await ResolveBookStartContextAsync(
            request.BookId,
            request.ChapterIndex,
            request.SegmentIndex,
            resumePositionMillisecondsOverride: null,
            allowSavedProgress: true,
            cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            PublishSnapshot(PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Faulted,
                Message = "未找到要打开的书籍。",
                CanRetry = false,
                CanSkip = false
            });
            return;
        }

        var speakSpeed = NormalizeSpeakSpeed(request.SpeakSpeedOverride ?? _currentSnapshot.SpeakSpeed);
        var selectedRule = await _selectedRuleProvider.GetSelectedRuleAsync(cancellationToken).ConfigureAwait(false);
        await OpenResolvedPositionAsync(
            resolved.Value.Book,
            resolved.Value.ChapterIndex,
            resolved.Value.SegmentIndex,
            resolved.Value.ResumePositionMilliseconds,
            selectedRule,
            speakSpeed,
            "已恢复到当前位置，等待播放。",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PauseCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentSession is null)
        {
            return;
        }

        if (!_currentSession.HasLoadedAudio)
        {
            PublishSnapshot(_currentSnapshot with
            {
                State = PlaybackState.Paused,
                Message = "已暂停，等待播放。"
            });
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

        await RefreshPrefetchWindowAsync(_currentSession, maxCountOverride: 1, cancellationToken);
        await SaveProgressAsync(_currentSession, _localAudioPlaybackCoordinator.CurrentSnapshot.PositionMilliseconds, cancellationToken);
    }

    private async Task ResumeCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentSession is null)
        {
            return;
        }

        if (!_currentSession.HasLoadedAudio)
        {
            var rule = _currentRule ?? await _selectedRuleProvider.GetSelectedRuleAsync(cancellationToken).ConfigureAwait(false);
            if (rule is null)
            {
                if (_currentBook is not null)
                {
                    PublishSnapshot(CreateRuleMissingSnapshot(
                        _currentBook,
                        _currentSession.ChapterIndex,
                        GetChapterTitle(_currentBook, _currentSession.ChapterIndex),
                        _currentSession.SegmentIndex,
                        _currentSession.SpeakSpeed));
                }

                return;
            }

            _currentRule = rule;
            await PlayCurrentSegmentAsync(
                _currentSession,
                _currentSession.ResumePositionMilliseconds,
                forceInvalidate: false,
                cancellationToken).ConfigureAwait(false);
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

        await RefreshPrefetchWindowAsync(_currentSession, maxCountOverride: null, cancellationToken);
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
        if (session.HasLoadedAudio)
        {
            await _localAudioPlaybackCoordinator.StopAsync(cancellationToken);
        }

        await SaveProgressAsync(session, GetCurrentPositionMillisecondsForSave(session), cancellationToken);
        await _prefetchScheduler.CancelAsync(session.SessionId, cancellationToken);
        await DisposeSessionAsync();
        ClearProtectedPlaybackFile();

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

    private async Task RefreshBookMetadataCoreAsync(string bookId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (_currentBook is null ||
            !string.Equals(_currentBook.BookId, bookId, StringComparison.Ordinal))
        {
            return;
        }

        var refreshedBook = await _bookContentService.GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (refreshedBook is null)
        {
            return;
        }

        _currentBook = MergeBookMetadata(_currentBook, refreshedBook);
        PublishSnapshot(_currentSnapshot with
        {
            BookTitle = _currentBook.BookTitle,
            BookAuthor = _currentBook.BookAuthor
        });
    }

    private async Task RefreshRegexReplacementCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null || _currentSession is null)
        {
            return;
        }

        var session = _currentSession;
        var chapterIndex = session.ChapterIndex;
        var previousChapter = GetChapter(_currentBook, chapterIndex);
        var previousSegment = previousChapter is not null && session.SegmentIndex >= 0 && session.SegmentIndex < previousChapter.Segments.Count
            ? previousChapter.Segments[session.SegmentIndex]
            : null;
        var characterOffset = previousSegment?.StartOffset ?? 0;
        var replacement = await _bookContentService.GetChapterAsync(_currentBook.BookId, chapterIndex, cancellationToken).ConfigureAwait(false);
        if (replacement is null)
        {
            return;
        }

        _currentBook = ReplaceChapter(_currentBook, replacement);
        _contentRevision++;
        var wasPlaying = _currentSnapshot.State == PlaybackState.Playing;

        if (replacement.LoadState == PlaybackChapterLoadState.LoadedEmpty)
        {
            var target = await ResolveNearestAvailablePositionAsync(_currentBook, chapterIndex, cancellationToken).ConfigureAwait(false);
            if (target is null || _currentRule is null)
            {
                if (session.HasLoadedAudio)
                {
                    await _localAudioPlaybackCoordinator.StopAsync(cancellationToken).ConfigureAwait(false);
                }

                await _prefetchScheduler.CancelAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
                await DisposeSessionAsync().ConfigureAwait(false);
                PublishSnapshot(_currentSnapshot with
                {
                    State = PlaybackState.Stopped,
                    SegmentCount = 0,
                    ContentRevision = _contentRevision,
                    Message = "正则替换后没有可播放的段落。",
                    CanRetry = false,
                    CanSkip = false
                });
                return;
            }

            await StartNewSessionAsync(
                target.Value.Book,
                target.Value.ChapterIndex,
                target.Value.SegmentIndex,
                0,
                _currentRule,
                session.SpeakSpeed,
                forceInvalidate: false,
                playImmediately: wasPlaying,
                pausedState: PlaybackState.Paused,
                pausedMessage: "正则替换规则已应用，等待播放。",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var mappedIndex = FindMappedSegmentIndex(replacement, characterOffset);
        var mappedSegment = replacement.Segments[mappedIndex];
        var speechChanged = previousSegment is null || !string.Equals(previousSegment.SpeechText, mappedSegment.SpeechText, StringComparison.Ordinal);
        if (!speechChanged)
        {
            await _prefetchScheduler.CancelAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            await RefreshPrefetchWindowAsync(session, null, cancellationToken).ConfigureAwait(false);
            PublishSnapshot(_currentSnapshot with
            {
                SegmentCount = replacement.Segments.Count,
                ContentRevision = _contentRevision,
                Message = "正则替换规则已应用。"
            });
            return;
        }

        if (_currentRule is null)
        {
            return;
        }

        await StartNewSessionAsync(
            _currentBook,
            chapterIndex,
            mappedIndex,
            0,
            _currentRule,
            session.SpeakSpeed,
            forceInvalidate: false,
            playImmediately: wasPlaying,
            pausedState: PlaybackState.Paused,
            pausedMessage: "正则替换规则已应用，等待播放。",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleBookDeletedCoreAsync(string bookId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (!string.Equals(_currentSnapshot.BookId, bookId, StringComparison.Ordinal))
        {
            return;
        }

        if (_currentSession is not null)
        {
            if (_currentSession.HasLoadedAudio)
            {
                await _localAudioPlaybackCoordinator.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            await _prefetchScheduler.CancelAsync(_currentSession.SessionId, cancellationToken).ConfigureAwait(false);
            await DisposeSessionAsync().ConfigureAwait(false);
        }

        ClearProtectedPlaybackFile();
        ClearCurrentBookContext();
        PublishSnapshot(PlaybackSnapshot.Idle);
    }

    private async Task MoveSegmentCoreAsync(int delta, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null)
        {
            return;
        }

        var currentPosition = GetCurrentPosition();
        var target = await ResolveRelativeSegmentAsync(
            _currentBook,
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            delta,
            cancellationToken);
        if (target is null)
        {
            return;
        }

        if (CurrentSnapshot.State == PlaybackState.Playing && _currentRule is not null)
        {
            await StartNewSessionAsync(
                target.Value.Book,
                target.Value.ChapterIndex,
                target.Value.SegmentIndex,
                0,
                _currentRule,
                GetCurrentSpeakSpeed(),
                forceInvalidate: false,
                playImmediately: true,
                pausedState: PlaybackState.Paused,
                pausedMessage: "已恢复到当前位置，等待播放。",
                cancellationToken);
            return;
        }

        var selectedRule = _currentRule ?? await _selectedRuleProvider.GetSelectedRuleAsync(cancellationToken).ConfigureAwait(false);
        await OpenResolvedPositionAsync(
            target.Value.Book,
            target.Value.ChapterIndex,
            target.Value.SegmentIndex,
            0,
            selectedRule,
            GetCurrentSpeakSpeed(),
            "已跳转到目标段落，等待播放。",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MoveChapterCoreAsync(int delta, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null)
        {
            return;
        }

        var currentPosition = GetCurrentPosition();
        var targetChapterIndex = FindRelativeChapterIndex(_currentBook, currentPosition.ChapterIndex, delta);
        if (targetChapterIndex is null)
        {
            return;
        }

        var target = await ResolvePlayablePositionAsync(
            _currentBook,
            targetChapterIndex.Value,
            0,
            delta >= 0 ? 1 : -1,
            preferLastSegmentWhenSearchingBackward: false,
            cancellationToken);
        if (target is null)
        {
            return;
        }

        if (CurrentSnapshot.State == PlaybackState.Playing && _currentRule is not null)
        {
            await StartNewSessionAsync(
                target.Value.Book,
                target.Value.ChapterIndex,
                target.Value.SegmentIndex,
                0,
                _currentRule,
                GetCurrentSpeakSpeed(),
                forceInvalidate: false,
                playImmediately: true,
                pausedState: PlaybackState.Paused,
                pausedMessage: "已恢复到当前位置，等待播放。",
                cancellationToken);
            return;
        }

        var selectedRule = _currentRule ?? await _selectedRuleProvider.GetSelectedRuleAsync(cancellationToken).ConfigureAwait(false);
        await OpenResolvedPositionAsync(
            target.Value.Book,
            target.Value.ChapterIndex,
            target.Value.SegmentIndex,
            0,
            selectedRule,
            GetCurrentSpeakSpeed(),
            "已跳转到目标章节，等待播放。",
            cancellationToken).ConfigureAwait(false);
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
            PublishSnapshot(CreateRuleMissingSnapshot(
                _currentBook,
                current.ChapterIndex,
                GetChapterTitle(_currentBook, current.ChapterIndex),
                current.SegmentIndex,
                GetCurrentSpeakSpeed()));
            return;
        }

        var currentPosition = GetCurrentPosition();
        await StartNewSessionAsync(
            await EnsureChapterLoadedAsync(_currentBook, currentPosition.ChapterIndex, cancellationToken),
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            0,
            rule,
            GetCurrentSpeakSpeed(),
            forceInvalidate: _lastFailureKind == TtsErrorKind.AudioDecode,
            playImmediately: true,
            pausedState: PlaybackState.Paused,
            pausedMessage: "已恢复到当前位置，等待播放。",
            cancellationToken);
    }

    private async Task SkipCurrentSegmentCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null)
        {
            return;
        }

        var currentPosition = GetCurrentPosition();
        var next = await ResolveRelativeSegmentAsync(
            _currentBook,
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            1,
            cancellationToken);
        if (next is null)
        {
            await StopCoreAsync(cancellationToken);
            PublishSnapshot(_currentSnapshot with
            {
                Message = "已跳过最后一段，播放结束。"
            });
            return;
        }

        if (_currentRule is null)
        {
            PublishSnapshot(CreateRuleMissingSnapshot(
                next.Value.Book,
                next.Value.ChapterIndex,
                GetChapterTitle(next.Value.Book, next.Value.ChapterIndex),
                next.Value.SegmentIndex,
                GetCurrentSpeakSpeed()));
            return;
        }

        await StartNewSessionAsync(
            next.Value.Book,
            next.Value.ChapterIndex,
            next.Value.SegmentIndex,
            0,
            _currentRule,
            GetCurrentSpeakSpeed(),
            forceInvalidate: false,
            playImmediately: true,
            pausedState: PlaybackState.Paused,
            pausedMessage: "已恢复到当前位置，等待播放。",
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
                PublishSnapshot(CreateRuleMissingSnapshot(
                    _currentBook,
                    current.ChapterIndex,
                    GetChapterTitle(_currentBook, current.ChapterIndex),
                    current.SegmentIndex,
                    GetCurrentSpeakSpeed()));
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
                HasAvailableRule = true,
                Message = $"已切换为规则：{rule.RuleName}"
            });
            return;
        }

        var currentPosition = GetCurrentPosition();
        var currentBook = await EnsureChapterLoadedAsync(_currentBook, currentPosition.ChapterIndex, cancellationToken).ConfigureAwait(false);
        if (CurrentSnapshot.State == PlaybackState.Playing)
        {
            await StartNewSessionAsync(
                currentBook,
                currentPosition.ChapterIndex,
                currentPosition.SegmentIndex,
                0,
                rule,
                GetCurrentSpeakSpeed(),
                forceInvalidate: false,
                playImmediately: true,
                pausedState: PlaybackState.Paused,
                pausedMessage: "已恢复到当前位置，等待播放。",
                cancellationToken);
            return;
        }

        await OpenResolvedPositionAsync(
            currentBook,
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            0,
            rule,
            GetCurrentSpeakSpeed(),
            "已切换规则，等待播放。",
            cancellationToken).ConfigureAwait(false);
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
        var currentBook = await EnsureChapterLoadedAsync(_currentBook, currentPosition.ChapterIndex, cancellationToken).ConfigureAwait(false);
        if (CurrentSnapshot.State == PlaybackState.Playing)
        {
            await StartNewSessionAsync(
                currentBook,
                currentPosition.ChapterIndex,
                currentPosition.SegmentIndex,
                0,
                _currentRule,
                normalizedSpeed,
                forceInvalidate: false,
                playImmediately: true,
                pausedState: PlaybackState.Paused,
                pausedMessage: "已恢复到当前位置，等待播放。",
                cancellationToken);
            return;
        }

        await OpenResolvedPositionAsync(
            currentBook,
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            0,
            _currentRule,
            normalizedSpeed,
            "语速已调整，等待播放。",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task StartNewSessionAsync(
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex,
        long resumePositionMilliseconds,
        SelectedPlaybackRule selectedRule,
        int speakSpeed,
        bool forceInvalidate,
        bool playImmediately,
        PlaybackState pausedState,
        string pausedMessage,
        CancellationToken cancellationToken)
    {
        if (_currentSession is not null)
        {
            await SaveProgressAsync(
                _currentSession,
                GetCurrentPositionMillisecondsForSave(_currentSession),
                cancellationToken).ConfigureAwait(false);

            // Stop the currently loaded local audio before we buffer a replacement segment.
            // Otherwise the old/intermediate segment can finish and advance the new session.
            if (_currentSession.HasLoadedAudio)
            {
                await _localAudioPlaybackCoordinator.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            ClearProtectedPlaybackFile();
        }

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
        session.ResumePositionMilliseconds = resumePositionMilliseconds;
        session.HasLoadedAudio = false;

        if (playImmediately)
        {
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
            return;
        }

        _currentBook = await EnsureChapterLoadedAsync(book, chapterIndex, cancellationToken).ConfigureAwait(false);
        PublishSnapshot(BuildSnapshot(
            pausedState,
            _currentBook,
            chapterIndex,
            segmentIndex,
            selectedRule,
            speakSpeed,
            resumePositionMilliseconds,
            0,
            pausedMessage,
            false,
            false,
            false));
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

        _currentBook = await EnsureChapterLoadedAsync(_currentBook, session.ChapterIndex, cancellationToken);
        var chapter = GetChapter(_currentBook, session.ChapterIndex);
        while (chapter is not null &&
               session.SegmentIndex >= 0 &&
               session.SegmentIndex < chapter.Segments.Count &&
               string.IsNullOrWhiteSpace(chapter.Segments[session.SegmentIndex].SpeechText))
        {
            var next = await ResolveRelativeSegmentAsync(
                _currentBook,
                session.ChapterIndex,
                session.SegmentIndex,
                1,
                cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                await _prefetchScheduler.CancelAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
                await DisposeSessionAsync().ConfigureAwait(false);
                PublishSnapshot(_currentSnapshot with
                {
                    State = PlaybackState.Stopped,
                    Message = "已跳过没有语音内容的段落，播放结束。",
                    CanRetry = false,
                    CanSkip = false
                });
                return;
            }

            _currentBook = next.Value.Book;
            session.ChapterIndex = next.Value.ChapterIndex;
            session.SegmentIndex = next.Value.SegmentIndex;
            session.ResumePositionMilliseconds = 0;
            chapter = GetChapter(_currentBook, session.ChapterIndex);
        }

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

        var audio = await _audioProvider.GetAudioAsync(
            audioRequest,
            PlaybackAudioPriority.Current,
            progress =>
            {
                if (!IsSessionCurrent(session.SessionId) || _currentBook is null || _currentRule is null)
                {
                    return;
                }

                PublishSnapshot(BuildSnapshot(
                    PlaybackState.Buffering,
                    _currentBook,
                    chapter.ChapterIndex,
                    session.SegmentIndex,
                    _currentRule,
                    session.SpeakSpeed,
                    0,
                    0,
                    progress.Message,
                    false,
                    false,
                    false));
            },
            linkedCts.Token);
        if (!IsSessionCurrent(session.SessionId))
        {
            return;
        }

        if (!audio.IsSuccess)
        {
            session.HasLoadedAudio = false;
            HandleSegmentFailure(audio.Failure!, canSkip: HasNextSegment(_currentBook, chapter.ChapterIndex, session.SegmentIndex));
            return;
        }

        session.ConsecutiveSegmentFailureCount = 0;
        _lastFailureKind = null;
        _lastRecoveredCorruptSegmentKey = null;
        session.ResumePositionMilliseconds = resumePositionMilliseconds;
        ReplaceProtectedPlaybackFile(audio.FilePath);

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
        if (local.State is PlaybackState.Faulted or PlaybackState.Stopped)
        {
            ClearProtectedPlaybackFile();
            session.HasLoadedAudio = false;
        }
        else
        {
            session.HasLoadedAudio = true;
            session.ResumePositionMilliseconds = local.PositionMilliseconds;
        }

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

        await RefreshPrefetchWindowAsync(session, maxCountOverride: null, linkedCts.Token);
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

    private async Task RefreshPrefetchWindowAsync(
        PlaybackSession session,
        int? maxCountOverride,
        CancellationToken cancellationToken)
    {
        if (_currentBook is null || _currentRule is null)
        {
            return;
        }

        var prefetchCount = await GetPrefetchCountAsync(maxCountOverride, cancellationToken).ConfigureAwait(false);
        if (prefetchCount <= 0)
        {
            await _prefetchScheduler.ScheduleAsync(session.SessionId, Array.Empty<PlaybackAudioRequest>(), cancellationToken).ConfigureAwait(false);
            return;
        }

        var requests = new List<PlaybackAudioRequest>();
        var book = _currentBook;
        var chapterIndex = session.ChapterIndex;
        var segmentIndex = session.SegmentIndex;
        while (requests.Count < prefetchCount)
        {
            var next = await ResolveRelativeSegmentAsync(
                book,
                chapterIndex,
                segmentIndex,
                1,
                cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                break;
            }

            book = next.Value.Book;
            chapterIndex = next.Value.ChapterIndex;
            segmentIndex = next.Value.SegmentIndex;
            AddPrefetchRequest(book, requests, session, chapterIndex, segmentIndex);
        }

        if (IsSessionCurrent(session.SessionId))
        {
            _currentBook = book;
        }

        await _prefetchScheduler.ScheduleAsync(session.SessionId, requests, cancellationToken).ConfigureAwait(false);
    }

    private void AddPrefetchRequest(
        PlaybackBookContent book,
        List<PlaybackAudioRequest> requests,
        PlaybackSession session,
        int chapterIndex,
        int segmentIndex)
    {
        if (_currentRule is null)
        {
            return;
        }

        var chapter = GetChapter(book, chapterIndex);
        if (chapter is null || segmentIndex < 0 || segmentIndex >= chapter.Segments.Count)
        {
            return;
        }

        var speechText = chapter.Segments[segmentIndex].SpeechText;
        if (string.IsNullOrWhiteSpace(speechText))
        {
            return;
        }

        requests.Add(new PlaybackAudioRequest(
            book.BookId,
            chapterIndex,
            segmentIndex,
            speechText,
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

            var next = await ResolveRelativeSegmentAsync(
                _currentBook,
                _currentSession.ChapterIndex,
                _currentSession.SegmentIndex,
                1,
                CancellationToken.None);
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

            _currentBook = next.Value.Book;
            _currentSession.ChapterIndex = next.Value.ChapterIndex;
            _currentSession.SegmentIndex = next.Value.SegmentIndex;
            _currentSession.ResumePositionMilliseconds = 0;
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
            _currentSession.ResumePositionMilliseconds = snapshot.PositionMilliseconds;
            return Task.CompletedTask;
        });
    }

    private static PlaybackSnapshot CreateRuleMissingSnapshot(
        PlaybackBookContent book,
        int chapterIndex,
        string? chapterTitle,
        int segmentIndex,
        int speakSpeed)
    {
        return new PlaybackSnapshot(
            PlaybackState.Stopped,
            book.BookId,
            book.BookTitle,
            chapterIndex,
            chapterTitle,
            segmentIndex,
            0,
            null,
            null,
            AppSettings.NormalizeSpeakSpeed(speakSpeed),
            0,
            0,
            "当前没有可用的 TTS 规则，请先前往规则页选择或导入规则。",
            false,
            false,
            false,
            book.BookAuthor,
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
            canSkip,
            book.BookAuthor,
            true,
            _contentRevision);
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
        return AppSettings.NormalizeSpeakSpeed(speakSpeed);
    }

    private Task<int> GetPrefetchCountAsync(int? maxCountOverride, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _appSettingsService.Current;
        var prefetchCount = Math.Clamp(settings.PrefetchCount, 0, AppSettings.DefaultPrefetchCountValue);
        return Task.FromResult(maxCountOverride is null
            ? prefetchCount
            : Math.Min(prefetchCount, maxCountOverride.Value));
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
        var chapter = GetChapter(book, chapterIndex);
        if (chapter is not null && segmentIndex + 1 < chapter.Segments.Count)
        {
            return true;
        }

        return FindRelativeChapterIndex(book, chapterIndex, 1) is not null;
    }

    private async Task<(PlaybackBookContent Book, PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex)?> ResolvePlayablePositionAsync(
        PlaybackBookContent book,
        int? preferredChapterIndex,
        int? preferredSegmentIndex,
        int searchDirection,
        bool preferLastSegmentWhenSearchingBackward,
        CancellationToken cancellationToken)
    {
        var orderedChapters = book.Chapters.OrderBy(chapter => chapter.ChapterIndex).ToArray();
        if (orderedChapters.Length == 0)
        {
            return null;
        }

        var index = ResolveChapterSearchStartIndex(orderedChapters, preferredChapterIndex, searchDirection);
        if (index < 0)
        {
            return null;
        }

        while (index >= 0 && index < orderedChapters.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapterIndex = orderedChapters[index].ChapterIndex;
            book = await EnsureChapterLoadedAsync(book, chapterIndex, cancellationToken);
            var chapter = GetChapter(book, chapterIndex);
            if (chapter?.LoadState == PlaybackChapterLoadState.Loaded)
            {
                var segmentIndex = ResolveSegmentIndex(
                    chapter,
                    preferredChapterIndex,
                    preferredSegmentIndex,
                    searchDirection,
                    preferLastSegmentWhenSearchingBackward);
                return (book, chapter, chapter.ChapterIndex, segmentIndex);
            }

            index += searchDirection >= 0 ? 1 : -1;
        }

        return null;
    }

    /// <summary>
    /// Finds the next chapter with runtime content, then falls back to the preceding chapter.
    /// This is used when a rule filters every consumable segment from the active chapter.
    /// </summary>
    private async Task<(PlaybackBookContent Book, PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex)?> ResolveNearestAvailablePositionAsync(
        PlaybackBookContent book,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        var nextChapterIndex = FindRelativeChapterIndex(book, chapterIndex, 1);
        if (nextChapterIndex is not null)
        {
            var next = await ResolvePlayablePositionAsync(
                book,
                nextChapterIndex.Value,
                preferredSegmentIndex: null,
                searchDirection: 1,
                preferLastSegmentWhenSearchingBackward: false,
                cancellationToken).ConfigureAwait(false);
            if (next is not null)
            {
                return next;
            }
        }

        var previousChapterIndex = FindRelativeChapterIndex(book, chapterIndex, -1);
        return previousChapterIndex is null
            ? null
            : await ResolvePlayablePositionAsync(
                book,
                previousChapterIndex.Value,
                preferredSegmentIndex: null,
                searchDirection: -1,
                preferLastSegmentWhenSearchingBackward: true,
                cancellationToken).ConfigureAwait(false);
    }

    private async Task<(PlaybackBookContent Book, PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex, long ResumePositionMilliseconds)?> ResolveRestoredPositionAsync(
        PlaybackBookContent book,
        ReadingProgressEntry progress,
        CancellationToken cancellationToken)
    {
        book = await EnsureChapterLoadedAsync(book, progress.ChapterIndex, cancellationToken).ConfigureAwait(false);
        var chapter = GetChapter(book, progress.ChapterIndex);
        if (chapter?.LoadState == PlaybackChapterLoadState.Loaded)
        {
            var remappedSegmentIndex = FindMappedSegmentIndex(chapter, progress.CharacterOffset);
            var resumePosition = progress.SegmentIndex >= 0 &&
                                 progress.SegmentIndex < chapter.Segments.Count &&
                                 chapter.Segments[progress.SegmentIndex].StartOffset == progress.CharacterOffset
                ? progress.AudioPositionMilliseconds
                : 0;
            return (book, chapter, chapter.ChapterIndex, remappedSegmentIndex, resumePosition);
        }

        var fallback = await ResolvePlayablePositionAsync(
            book,
            progress.ChapterIndex,
            progress.SegmentIndex,
            searchDirection: 1,
            preferLastSegmentWhenSearchingBackward: false,
            cancellationToken).ConfigureAwait(false);
        return fallback is null
            ? null
            : (fallback.Value.Book, fallback.Value.Chapter, fallback.Value.ChapterIndex, fallback.Value.SegmentIndex, 0);
    }

    private async Task<(PlaybackBookContent Book, int ChapterIndex, int SegmentIndex)?> ResolveRelativeSegmentAsync(
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex,
        int delta,
        CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return (book, chapterIndex, segmentIndex);
        }

        book = await EnsureChapterLoadedAsync(book, chapterIndex, cancellationToken);
        var chapter = GetChapter(book, chapterIndex);
        if (chapter?.LoadState != PlaybackChapterLoadState.Loaded)
        {
            return null;
        }

        var targetSegmentIndex = segmentIndex + delta;
        if (targetSegmentIndex >= 0 && targetSegmentIndex < chapter.Segments.Count)
        {
            return (book, chapterIndex, targetSegmentIndex);
        }

        var targetChapterIndex = FindRelativeChapterIndex(book, chapterIndex, delta > 0 ? 1 : -1);
        if (targetChapterIndex is null)
        {
            return null;
        }

        var target = await ResolvePlayablePositionAsync(
            book,
            targetChapterIndex.Value,
            preferredSegmentIndex: null,
            searchDirection: delta > 0 ? 1 : -1,
            preferLastSegmentWhenSearchingBackward: delta < 0,
            cancellationToken);
        return target is null
            ? null
            : (target.Value.Book, target.Value.ChapterIndex, target.Value.SegmentIndex);
    }

    private static int? FindRelativeChapterIndex(
        PlaybackBookContent book,
        int chapterIndex,
        int delta)
    {
        var chapters = book.Chapters
            .OrderBy(chapter => chapter.ChapterIndex)
            .ToArray();

        var currentIndex = Array.FindIndex(chapters, chapter => chapter.ChapterIndex == chapterIndex);
        if (currentIndex < 0)
        {
            return chapters.Length == 0 ? null : chapters[0].ChapterIndex;
        }

        var targetIndex = currentIndex + delta;
        return targetIndex < 0 || targetIndex >= chapters.Length
            ? null
            : chapters[targetIndex].ChapterIndex;
    }

    private async Task<PlaybackBookContent> EnsureChapterLoadedAsync(
        PlaybackBookContent book,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        var existing = GetChapter(book, chapterIndex);
        if (existing is null || existing.LoadState != PlaybackChapterLoadState.Unloaded)
        {
            return book;
        }

        var loadedChapter = await _bookContentService.GetChapterAsync(book.BookId, chapterIndex, cancellationToken);
        return ReplaceChapter(
            book,
            loadedChapter ?? PlaybackChapterContent.Failed(existing.ChapterIndex, existing.Title));
    }

    private static PlaybackBookContent ReplaceChapter(PlaybackBookContent book, PlaybackChapterContent chapter)
    {
        var chapters = book.Chapters
            .Select(existing => existing.ChapterIndex == chapter.ChapterIndex ? chapter : existing)
            .ToArray();

        return book with { Chapters = chapters };
    }

    private static int ResolveChapterSearchStartIndex(
        IReadOnlyList<PlaybackChapterContent> chapters,
        int? preferredChapterIndex,
        int searchDirection)
    {
        if (chapters.Count == 0)
        {
            return -1;
        }

        if (preferredChapterIndex is null)
        {
            return searchDirection >= 0 ? 0 : chapters.Count - 1;
        }

        if (searchDirection >= 0)
        {
            for (var index = 0; index < chapters.Count; index++)
            {
                if (chapters[index].ChapterIndex >= preferredChapterIndex.Value)
                {
                    return index;
                }
            }

            return -1;
        }

        for (var index = chapters.Count - 1; index >= 0; index--)
        {
            if (chapters[index].ChapterIndex <= preferredChapterIndex.Value)
            {
                return index;
            }
        }

        return -1;
    }

    private static int ResolveSegmentIndex(
        PlaybackChapterContent chapter,
        int? preferredChapterIndex,
        int? preferredSegmentIndex,
        int searchDirection,
        bool preferLastSegmentWhenSearchingBackward)
    {
        if (preferredChapterIndex == chapter.ChapterIndex && preferredSegmentIndex is >= 0 and < int.MaxValue)
        {
            return Math.Min(preferredSegmentIndex.Value, chapter.Segments.Count - 1);
        }

        if (searchDirection < 0 && preferLastSegmentWhenSearchingBackward)
        {
            return chapter.Segments.Count - 1;
        }

        return 0;
    }

    private static int FindMappedSegmentIndex(PlaybackChapterContent chapter, int characterOffset)
    {
        for (var index = 0; index < chapter.Segments.Count; index++)
        {
            if (chapter.Segments[index].StartOffset >= characterOffset)
            {
                return index;
            }
        }

        return chapter.Segments.Count - 1;
    }

    private bool IsSessionCurrent(Guid sessionId)
    {
        return _currentSession is not null && _currentSession.SessionId == sessionId;
    }

    private long GetCurrentPositionMillisecondsForSave(PlaybackSession session)
    {
        return session.HasLoadedAudio
            ? _localAudioPlaybackCoordinator.CurrentSnapshot.PositionMilliseconds
            : session.ResumePositionMilliseconds;
    }

    private async Task SaveProgressAsync(PlaybackSession session, long positionMilliseconds, CancellationToken cancellationToken)
    {
        await _readingProgressStore.SaveAsync(
            new PlaybackProgressUpdate(
                session.BookId,
                session.ChapterIndex,
                session.SegmentIndex,
                GetCharacterOffset(session),
                positionMilliseconds),
            cancellationToken);
    }

    private int GetCharacterOffset(PlaybackSession session)
    {
        var chapter = _currentBook is null
            ? null
            : GetChapter(_currentBook, session.ChapterIndex);
        if (chapter is null || session.SegmentIndex < 0 || session.SegmentIndex >= chapter.Segments.Count)
        {
            return 0;
        }

        return chapter.Segments[session.SegmentIndex].StartOffset;
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

    private void ClearCurrentBookContext()
    {
        _currentBook = null;
        _currentRule = null;
        _lastFailureKind = null;
        _lastRecoveredCorruptSegmentKey = null;
    }

    private static PlaybackBookContent MergeBookMetadata(PlaybackBookContent existingBook, PlaybackBookContent refreshedBook)
    {
        var existingChapters = existingBook.Chapters.ToDictionary(chapter => chapter.ChapterIndex);
        var mergedChapters = refreshedBook.Chapters
            .Select(chapter => existingChapters.GetValueOrDefault(chapter.ChapterIndex) ?? chapter)
            .ToArray();

        return new PlaybackBookContent(
            refreshedBook.BookId,
            refreshedBook.BookTitle,
            mergedChapters,
            refreshedBook.BookAuthor);
    }

    private void ReplaceProtectedPlaybackFile(string? filePath)
    {
        ClearProtectedPlaybackFile();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _currentAudioProtection = _audioCacheProtectionRegistry.Protect(filePath);
        }
    }

    private void ClearProtectedPlaybackFile()
    {
        _currentAudioProtection?.Dispose();
        _currentAudioProtection = null;
    }

    private async Task<(PlaybackBookContent Book, PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex, long ResumePositionMilliseconds)?> ResolveBookStartContextAsync(
        string bookId,
        int? requestedChapterIndex,
        int? requestedSegmentIndex,
        long? resumePositionMillisecondsOverride,
        bool allowSavedProgress,
        CancellationToken cancellationToken)
    {
        var book = await _bookContentService.GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        var hasExplicitPosition = requestedChapterIndex is not null || requestedSegmentIndex is not null;
        var resumePositionMilliseconds = resumePositionMillisecondsOverride ?? 0;
        (PlaybackBookContent Book, PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex)? startPosition = null;

        if (allowSavedProgress && !hasExplicitPosition)
        {
            var savedProgress = await _readingProgressStore.GetAsync(bookId, cancellationToken).ConfigureAwait(false);
            if (savedProgress is not null)
            {
                var restoredPosition = await ResolveRestoredPositionAsync(book, savedProgress, cancellationToken).ConfigureAwait(false);
                if (restoredPosition is not null)
                {
                    book = restoredPosition.Value.Book;
                    startPosition = (
                        restoredPosition.Value.Book,
                        restoredPosition.Value.Chapter,
                        restoredPosition.Value.ChapterIndex,
                        restoredPosition.Value.SegmentIndex);
                    resumePositionMilliseconds = resumePositionMillisecondsOverride ?? restoredPosition.Value.ResumePositionMilliseconds;
                }
            }
        }

        startPosition ??= await ResolvePlayablePositionAsync(
            book,
            requestedChapterIndex,
            requestedSegmentIndex,
            searchDirection: 1,
            preferLastSegmentWhenSearchingBackward: false,
            cancellationToken).ConfigureAwait(false);
        if (startPosition is null)
        {
            return null;
        }

        return (
            startPosition.Value.Book,
            startPosition.Value.Chapter,
            startPosition.Value.ChapterIndex,
            startPosition.Value.SegmentIndex,
            resumePositionMilliseconds);
    }

    private async Task OpenResolvedPositionAsync(
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex,
        long resumePositionMilliseconds,
        SelectedPlaybackRule? selectedRule,
        int speakSpeed,
        string pausedMessage,
        CancellationToken cancellationToken)
    {
        if (selectedRule is null)
        {
            if (_currentSession is not null)
            {
                await SaveProgressAsync(_currentSession, GetCurrentPositionMillisecondsForSave(_currentSession), cancellationToken).ConfigureAwait(false);
            }

            if (_currentSession?.HasLoadedAudio == true)
            {
                await _localAudioPlaybackCoordinator.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            await _prefetchScheduler.CancelAsync(_currentSession?.SessionId ?? Guid.Empty, cancellationToken).ConfigureAwait(false);
            await DisposeSessionAsync().ConfigureAwait(false);
            ClearProtectedPlaybackFile();

            _currentBook = await EnsureChapterLoadedAsync(book, chapterIndex, cancellationToken).ConfigureAwait(false);
            _currentRule = null;
            PublishSnapshot(CreateRuleMissingSnapshot(
                _currentBook,
                chapterIndex,
                GetChapterTitle(_currentBook, chapterIndex),
                segmentIndex,
                speakSpeed));
            return;
        }

        await StartNewSessionAsync(
            book,
            chapterIndex,
            segmentIndex,
            resumePositionMilliseconds,
            selectedRule,
            speakSpeed,
            forceInvalidate: false,
            playImmediately: false,
            pausedState: PlaybackState.Paused,
            pausedMessage: pausedMessage,
            cancellationToken);
    }

    private async Task JumpToCoreAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null)
        {
            return;
        }

        var target = await ResolvePlayablePositionAsync(
            _currentBook,
            chapterIndex,
            segmentIndex,
            searchDirection: 1,
            preferLastSegmentWhenSearchingBackward: false,
            cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return;
        }

        if (_currentSnapshot.State == PlaybackState.Playing && _currentRule is not null)
        {
            await StartNewSessionAsync(
                target.Value.Book,
                target.Value.ChapterIndex,
                target.Value.SegmentIndex,
                0,
                _currentRule,
                GetCurrentSpeakSpeed(),
                forceInvalidate: false,
                playImmediately: true,
                pausedState: PlaybackState.Paused,
                pausedMessage: "已恢复到当前位置，等待播放。",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var selectedRule = _currentRule ?? await _selectedRuleProvider.GetSelectedRuleAsync(cancellationToken).ConfigureAwait(false);
        await OpenResolvedPositionAsync(
            target.Value.Book,
            target.Value.ChapterIndex,
            target.Value.SegmentIndex,
            0,
            selectedRule,
            GetCurrentSpeakSpeed(),
            "已跳转到目标段落，等待播放。",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task JumpToChapterCoreAsync(int chapterIndex, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_currentBook is null)
        {
            return;
        }

        var target = await ResolvePlayablePositionAsync(
            _currentBook,
            chapterIndex,
            0,
            searchDirection: 1,
            preferLastSegmentWhenSearchingBackward: false,
            cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return;
        }

        await JumpToCoreAsync(target.Value.ChapterIndex, target.Value.SegmentIndex, cancellationToken).ConfigureAwait(false);
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
