using System.Collections.Concurrent;
using System.Threading.Channels;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Coordinates book-oriented playback sessions on top of the low-level local audio pipeline.
/// </summary>
public sealed class PlaybackCoordinator :
    IPlaybackSnapshotSource,
    IPlaybackSession,
    IPlaybackStopTimer,
    IPlaybackBookCommands,
    IPlaybackRegexReplacementRefresher,
    IAsyncDisposable
{
    private readonly IBookPlaybackContentService _bookContentService;
    private readonly ISelectedTtsRuleProvider _selectedRuleProvider;
    private readonly PlaybackSegmentRunner _segmentRunner;
    private readonly PlaybackRecoveryPolicy _recoveryPolicy;
    private readonly IAudioCacheProtectionRegistry _audioCacheProtectionRegistry;
    private readonly ILocalAudioPlaybackCoordinator _localAudioPlaybackCoordinator;
    private readonly PlaybackProgressService _progressService;
    private readonly IPlaybackPrefetchController _prefetchController;
    private readonly IAppSettingsService _appSettingsService;
    private readonly PlaybackStopTimerController _stopTimer;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Channel<PlaybackEventCommand> _eventCommands = Channel.CreateUnbounded<PlaybackEventCommand>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
    private readonly ConcurrentDictionary<PlaybackEventKey, byte> _pendingEventCommands = new();
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly CancellationTokenSource _eventCommandCancellation = new();
    private readonly Task _eventCommandProcessor;
    private readonly object _disposeGate = new();

    private PlaybackSnapshot _currentSnapshot = PlaybackSnapshot.Idle;
    private PlaybackSessionState? _currentSession;
    private TtsErrorKind? _lastFailureKind;
    private string? _lastRecoveredCorruptSegmentKey;
    private long _contentRevision;
    private bool _disposed;
    private Task? _disposeTask;

    // These accessors are aliases into the session owner. They intentionally do not
    // cache a second book, rule, or protection handle in the coordinator.
    private PlaybackBookContent? _currentBook
    {
        get => _currentSession?.Book;
        set
        {
            if (value is not null && _currentSession is not null)
            {
                _currentSession.Book = value;
            }
        }
    }

    private SelectedPlaybackRule? _currentRule
    {
        get => _currentSession?.Rule;
        set => _currentSession?.SetRule(value);
    }

    internal PlaybackCoordinator(
        IBookPlaybackContentService bookContentService,
        ISelectedTtsRuleProvider selectedRuleProvider,
        PlaybackSegmentRunner segmentRunner,
        PlaybackRecoveryPolicy recoveryPolicy,
        IAudioCacheProtectionRegistry audioCacheProtectionRegistry,
        ILocalAudioPlaybackCoordinator localAudioPlaybackCoordinator,
        PlaybackProgressService progressService,
        IPlaybackPrefetchController prefetchController,
        IAppSettingsService appSettingsService,
        TimeProvider timeProvider)
    {
        _bookContentService = bookContentService;
        _selectedRuleProvider = selectedRuleProvider;
        _segmentRunner = segmentRunner;
        _recoveryPolicy = recoveryPolicy;
        _audioCacheProtectionRegistry = audioCacheProtectionRegistry;
        _localAudioPlaybackCoordinator = localAudioPlaybackCoordinator;
        _progressService = progressService;
        _prefetchController = prefetchController;
        _appSettingsService = appSettingsService;
        _stopTimer = new PlaybackStopTimerController(
            timeProvider,
            PauseAsync,
            PublishStopTimerFailureSafely);

        _localAudioPlaybackCoordinator.SnapshotChanged += OnLocalSnapshotChanged;
        _localAudioPlaybackCoordinator.PlaybackCompleted += OnLocalPlaybackCompleted;
        _localAudioPlaybackCoordinator.PlaybackFailed += OnLocalPlaybackFailed;
        _eventCommandProcessor = ProcessEventCommandsAsync();
    }

    public PlaybackSnapshot CurrentSnapshot => _currentSnapshot;

    public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    PlaybackStopTimerSnapshot IPlaybackStopTimer.CurrentSnapshot => _stopTimer.CurrentSnapshot;

    event EventHandler<PlaybackStopTimerSnapshot>? IPlaybackStopTimer.SnapshotChanged
    {
        add => _stopTimer.SnapshotChanged += value;
        remove => _stopTimer.SnapshotChanged -= value;
    }

    void IPlaybackStopTimer.ScheduleAfter(TimeSpan duration) => _stopTimer.ScheduleAfter(duration);

    void IPlaybackStopTimer.ScheduleAtEndOfSegment() => _stopTimer.ScheduleAtEndOfSegment();

    void IPlaybackStopTimer.ScheduleAtEndOfChapter() => _stopTimer.ScheduleAtEndOfChapter();

    void IPlaybackStopTimer.Cancel() => _stopTimer.Cancel();

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

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        await _stopTimer.DisposeAsync().ConfigureAwait(false);
        _lifecycleCancellation.Cancel();
        _eventCommandCancellation.Cancel();
        _eventCommands.Writer.TryComplete();
        _currentSession?.Cancel();
        _localAudioPlaybackCoordinator.SnapshotChanged -= OnLocalSnapshotChanged;
        _localAudioPlaybackCoordinator.PlaybackCompleted -= OnLocalPlaybackCompleted;
        _localAudioPlaybackCoordinator.PlaybackFailed -= OnLocalPlaybackFailed;

        Exception? disposeFailure = null;
        var entered = false;
        try
        {
            await _mutex.WaitAsync().ConfigureAwait(false);
            entered = true;
            var session = _currentSession;
            if (session is not null)
            {
                try
                {
                    await SaveProgressAsync(
                        session,
                        session.HasLoadedAudio
                            ? _localAudioPlaybackCoordinator.CurrentSnapshot.PositionMilliseconds
                            : GetCurrentPositionMillisecondsForSave(session),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    disposeFailure ??= exception;
                }

                try
                {
                    await _prefetchController.CancelAsync(session.SessionId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    disposeFailure ??= exception;
                }

                try
                {
                    await DisposeSessionAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    disposeFailure ??= exception;
                }
            }

            try
            {
                ClearProtectedPlaybackFile();
            }
            catch (Exception exception)
            {
                disposeFailure ??= exception;
            }
        }
        catch (Exception exception)
        {
            disposeFailure ??= exception;
        }
        finally
        {
            if (entered)
            {
                _mutex.Release();
            }
        }

        try
        {
            await _eventCommandProcessor.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            disposeFailure ??= exception;
        }

        try
        {
            await _localAudioPlaybackCoordinator.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            disposeFailure ??= exception;
        }

        _eventCommandCancellation.Dispose();
        _lifecycleCancellation.Dispose();

        if (disposeFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(disposeFailure).Throw();
        }
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
                CanRetry = false
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
                CanRetry = false
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
            await RefreshPrefetchWindowAsync(_currentSession, maxCountOverride: 1, cancellationToken);
            await SaveProgressAsync(
                _currentSession,
                _currentSession.PositionForSave,
                cancellationToken);
            return;
        }

        await _localAudioPlaybackCoordinator.PauseAsync(cancellationToken);
        var pausedAudio = _localAudioPlaybackCoordinator.CurrentSnapshot;
        _currentSession.UpdateAudio(pausedAudio);
        if (_currentBook is not null && _currentRule is not null)
        {
            PublishSnapshot(BuildSnapshot(
                PlaybackState.Paused,
                _currentBook,
                _currentSession.ChapterIndex,
                _currentSession.SegmentIndex,
                _currentRule,
                _currentSession.SpeakSpeed,
                pausedAudio.PositionMilliseconds,
                pausedAudio.DurationMilliseconds,
                pausedAudio.Message,
                pausedAudio.IsUsingCache,
                false));
        }

        await RefreshPrefetchWindowAsync(_currentSession, maxCountOverride: 1, cancellationToken);
        await SaveProgressAsync(
            _currentSession,
            _localAudioPlaybackCoordinator.CurrentSnapshot.PositionMilliseconds,
            cancellationToken);
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
        var resumedAudio = _localAudioPlaybackCoordinator.CurrentSnapshot;
        _currentSession.UpdateAudio(resumedAudio);
        if (_currentBook is not null && _currentRule is not null)
        {
            PublishSnapshot(BuildSnapshot(
                PlaybackState.Playing,
                _currentBook,
                _currentSession.ChapterIndex,
                _currentSession.SegmentIndex,
                _currentRule,
                _currentSession.SpeakSpeed,
                resumedAudio.PositionMilliseconds,
                resumedAudio.DurationMilliseconds,
                resumedAudio.Message,
                resumedAudio.IsUsingCache,
                false));
        }

        await RefreshPrefetchWindowAsync(_currentSession, maxCountOverride: null, cancellationToken);
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _stopTimer.Cancel();
        if (_currentSession is null)
        {
            PublishSnapshot(PlaybackSnapshot.Idle);
            return;
        }

        var session = _currentSession;
        if (session.HasLoadedAudio)
        {
            session.UpdateAudio(_localAudioPlaybackCoordinator.CurrentSnapshot);
        }

        var positionBeforeStop = GetCurrentPositionMillisecondsForSave(session);
        if (session.HasLoadedAudio)
        {
            await _localAudioPlaybackCoordinator.StopAsync(cancellationToken);
            session.UpdateAudio(_localAudioPlaybackCoordinator.CurrentSnapshot);
        }

        await SaveProgressAsync(session, positionBeforeStop, cancellationToken);
        await _prefetchController.CancelAsync(session.SessionId, cancellationToken);
        await DisposeSessionAsync();
        ClearProtectedPlaybackFile();

        PublishSnapshot(_currentSnapshot with
        {
            State = PlaybackState.Stopped,
            PositionMilliseconds = 0,
            DurationMilliseconds = _localAudioPlaybackCoordinator.CurrentSnapshot.DurationMilliseconds,
            Message = "已停止当前播放。",
            CanRetry = false
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

        if (replacement.LoadState == PlaybackChapterLoadState.LoadedEmpty ||
            PlaybackPositionResolver.FindMappedSegmentIndex(replacement, characterOffset) < 0)
        {
            var target = await ResolveNearestAvailablePositionAsync(_currentBook, chapterIndex, cancellationToken).ConfigureAwait(false);
            if (target is null || _currentRule is null)
            {
                if (session.HasLoadedAudio)
                {
                    await _localAudioPlaybackCoordinator.StopAsync(cancellationToken).ConfigureAwait(false);
                }

                await _prefetchController.CancelAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
                await DisposeSessionAsync().ConfigureAwait(false);
                PublishSnapshot(_currentSnapshot with
                {
                    State = PlaybackState.Stopped,
                    SegmentCount = 0,
                    ContentRevision = _contentRevision,
                    Message = "正则替换后没有可播放的段落。",
                    CanRetry = false
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

        var mappedIndex = PlaybackPositionResolver.FindMappedSegmentIndex(replacement, characterOffset);
        var mappedSegment = replacement.Segments[mappedIndex];
        var speechChanged = previousSegment is null || !string.Equals(previousSegment.SpeechText, mappedSegment.SpeechText, StringComparison.Ordinal);
        if (!speechChanged)
        {
            await _prefetchController.CancelAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
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

        _stopTimer.Cancel();
        if (_currentSession is not null)
        {
            if (_currentSession.HasLoadedAudio)
            {
                await _localAudioPlaybackCoordinator.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            await _prefetchController.CancelAsync(_currentSession.SessionId, cancellationToken).ConfigureAwait(false);
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
                current.SegmentIndex,
                GetCurrentSpeakSpeed()));
            return;
        }

        var currentPosition = GetCurrentPosition();
        var previousFailureCount = _currentSession?.ConsecutiveSegmentFailureCount ?? 0;
        var retryDecision = _recoveryPolicy.Decide(new PlaybackRecoveryInput(
            _lastFailureKind ?? TtsErrorKind.Unknown,
            _currentSnapshot.Message ?? "正在重试当前段落。",
            _currentSession?.ConsecutiveSegmentFailureCount ?? 0,
            IsCorruptAudio: _lastFailureKind == TtsErrorKind.AudioDecode,
            CorruptAudioRecoveryAttempted: false));
        await StartNewSessionAsync(
            await EnsureChapterLoadedAsync(_currentBook, currentPosition.ChapterIndex, cancellationToken),
            currentPosition.ChapterIndex,
            currentPosition.SegmentIndex,
            0,
            rule,
            GetCurrentSpeakSpeed(),
            forceInvalidate: retryDecision.ShouldInvalidateAudio,
            playImmediately: true,
            pausedState: PlaybackState.Paused,
            pausedMessage: "已恢复到当前位置，等待播放。",
            cancellationToken,
            initialConsecutiveFailureCount: previousFailureCount);
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
        SelectedPlaybackRule? selectedRule,
        int speakSpeed,
        bool forceInvalidate,
        bool playImmediately,
        PlaybackState pausedState,
        string pausedMessage,
        CancellationToken cancellationToken,
        int initialConsecutiveFailureCount = 0)
    {
        _stopTimer.Cancel();
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

        await _prefetchController.CancelAsync(_currentSession?.SessionId ?? Guid.Empty, cancellationToken);
        await DisposeSessionAsync();

        var session = new PlaybackSessionState(
            book,
            chapterIndex,
            segmentIndex,
            selectedRule,
            speakSpeed);

        _currentSession = session;
        _currentBook = book;
        _currentRule = selectedRule;
        _lastFailureKind = null;
        _lastRecoveredCorruptSegmentKey = null;
        session.ResumePositionMilliseconds = resumePositionMilliseconds;
        session.ConsecutiveSegmentFailureCount = initialConsecutiveFailureCount;

        if (selectedRule is null)
        {
            _currentBook = await EnsureChapterLoadedAsync(book, chapterIndex, cancellationToken).ConfigureAwait(false);
            PublishSnapshot(CreateRuleMissingSnapshot(
                _currentBook,
                chapterIndex,
                segmentIndex,
                speakSpeed));
            return;
        }

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
            false));
    }

    private async Task PlayCurrentSegmentAsync(
        PlaybackSessionState session,
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
                await _prefetchController.CancelAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
                await DisposeSessionAsync().ConfigureAwait(false);
                PublishSnapshot(_currentSnapshot with
                {
                    State = PlaybackState.Stopped,
                    Message = "已跳过没有语音内容的段落，播放结束。",
                    CanRetry = false
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
            PublishPlaybackFailure("未找到要播放的章节。", TtsErrorKind.InvalidRule);
            return;
        }

        if (session.SegmentIndex < 0 || session.SegmentIndex >= chapter.Segments.Count)
        {
            PublishPlaybackFailure("未找到要播放的段落。", TtsErrorKind.InvalidRule);
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
            false));

        if (forceInvalidate)
        {
            PublishSnapshot(_currentSnapshot with { State = PlaybackState.Recovering, Message = "检测到音频损坏，正在重新生成。" });
        }

        var run = await _segmentRunner.RunAsync(
            new PlaybackSegmentRunRequest(
                audioRequest,
                $"{_currentBook.BookTitle} · {chapter.Title}",
                resumePositionMilliseconds,
                forceInvalidate),
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
                    false));
            },
            linkedCts.Token).ConfigureAwait(false);
        var audio = run.Audio;
        if (!IsSessionCurrent(session.SessionId))
        {
            return;
        }

        if (!audio.IsSuccess)
        {
            if (audio.Failure?.Kind == TtsErrorKind.Cancelled)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                return;
            }

            HandleSegmentFailure(audio.Failure!);
            return;
        }

        session.ConsecutiveSegmentFailureCount = 0;
        _lastFailureKind = null;
        _lastRecoveredCorruptSegmentKey = null;
        session.ResumePositionMilliseconds = resumePositionMilliseconds;
        ReplaceProtectedPlaybackFile(audio.FilePath);

        var local = run.LocalSnapshot;
        session.UpdateAudio(local);
        if (local.State is PlaybackState.Faulted or PlaybackState.Stopped)
        {
            ClearProtectedPlaybackFile();
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
            false));

        await RefreshPrefetchWindowAsync(session, maxCountOverride: null, linkedCts.Token);
    }

    private void HandleSegmentFailure(TtsExecutionFailure failure)
    {
        if (_currentSession is null)
        {
            PublishPlaybackFailure(failure.Message, failure.Kind);
            return;
        }

        var decision = _recoveryPolicy.Decide(new PlaybackRecoveryInput(
            failure.Kind,
            failure.Message,
            _currentSession.ConsecutiveSegmentFailureCount,
            IsCorruptAudio: false,
            CorruptAudioRecoveryAttempted: false));
        _currentSession.ConsecutiveSegmentFailureCount = decision.ConsecutiveSegmentFailureCount;
        _lastFailureKind = failure.Kind;
        PublishSnapshot(_currentSnapshot with
        {
            State = PlaybackState.Faulted,
            Message = decision.Message,
            CanRetry = decision.CanRetry
        });
    }

    private void PublishPlaybackFailure(string message, TtsErrorKind failureKind)
    {
        _lastFailureKind = failureKind;
        PublishSnapshot(_currentSnapshot with
        {
            State = PlaybackState.Faulted,
            Message = message,
            CanRetry = true
        });
    }

    private async Task RefreshPrefetchWindowAsync(
        PlaybackSessionState session,
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
            await _prefetchController.SubmitAsync(
                new PlaybackPrefetchWindow(session.SessionId, Array.Empty<PlaybackAudioRequest>()),
                cancellationToken).ConfigureAwait(false);
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

        await _prefetchController.SubmitAsync(
            new PlaybackPrefetchWindow(session.SessionId, requests),
            cancellationToken).ConfigureAwait(false);
    }

    private void AddPrefetchRequest(
        PlaybackBookContent book,
        List<PlaybackAudioRequest> requests,
        PlaybackSessionState session,
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

    private void OnLocalPlaybackCompleted(object? sender, EventArgs e)
    {
        EnqueueEventCommand(new PlaybackEventCommand(
            PlaybackEventCommandKind.Completed,
            _currentSession?.SessionId,
            _localAudioPlaybackCoordinator.CurrentSnapshot,
            null));
    }

    private void OnLocalPlaybackFailed(object? sender, PlaybackErrorEventArgs error)
    {
        EnqueueEventCommand(new PlaybackEventCommand(
            PlaybackEventCommandKind.Failed,
            _currentSession?.SessionId,
            _localAudioPlaybackCoordinator.CurrentSnapshot,
            error));
    }

    private void OnLocalSnapshotChanged(object? sender, LocalAudioPlaybackSnapshot snapshot)
    {
        EnqueueEventCommand(new PlaybackEventCommand(
            PlaybackEventCommandKind.SnapshotChanged,
            _currentSession?.SessionId,
            snapshot,
            null));
    }

    private void EnqueueEventCommand(PlaybackEventCommand command)
    {
        if (_disposed || command.SessionId is null)
        {
            return;
        }

        if (!_pendingEventCommands.TryAdd(command.Key, 0))
        {
            return;
        }

        if (!_eventCommands.Writer.TryWrite(command))
        {
            _pendingEventCommands.TryRemove(command.Key, out _);
        }
    }

    private async Task ProcessEventCommandsAsync()
    {
        try
        {
            await foreach (var command in _eventCommands.Reader.ReadAllAsync(_eventCommandCancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await ProcessEventCommandAsync(command).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    _disposed ||
                    _eventCommandCancellation.IsCancellationRequested ||
                    !IsSessionCurrent(command.SessionId ?? Guid.Empty))
                {
                    // Closing and session replacement are normal event invalidation paths.
                }
                catch (Exception)
                {
                    PublishEventCommandFailureSafely();
                }
                finally
                {
                    _pendingEventCommands.TryRemove(command.Key, out _);
                }
            }
        }
        catch (OperationCanceledException) when (_eventCommandCancellation.IsCancellationRequested)
        {
            // Closing cancels the owned command processor.
        }
    }

    private async Task ProcessEventCommandAsync(PlaybackEventCommand command)
    {
        if (_disposed || command.SessionId is not Guid sessionId)
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifecycleCancellation.Token,
            _currentSession?.CancellationToken ?? CancellationToken.None);
        await _mutex.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            if (_disposed || !IsSessionCurrent(sessionId) || command.Snapshot is null)
            {
                return;
            }

            var session = _currentSession!;
            if (!IsCurrentLocalAudioEvent(command.Snapshot))
            {
                return;
            }

            switch (command.Kind)
            {
                case PlaybackEventCommandKind.Completed:
                    await ProcessPlaybackCompletedAsync(session, command.Snapshot, linkedCancellation.Token).ConfigureAwait(false);
                    break;
                case PlaybackEventCommandKind.Failed:
                    await ProcessPlaybackFailedAsync(session, command.Error!, command.Snapshot, linkedCancellation.Token).ConfigureAwait(false);
                    break;
                case PlaybackEventCommandKind.SnapshotChanged:
                    ProcessSnapshotChanged(session, command.Snapshot);
                    break;
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task ProcessPlaybackCompletedAsync(
        PlaybackSessionState session,
        LocalAudioPlaybackSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (_currentBook is null)
        {
            return;
        }

        session.UpdateAudio(snapshot);
        var next = await ResolveRelativeSegmentAsync(
            _currentBook,
            session.ChapterIndex,
            session.SegmentIndex,
            1,
            cancellationToken).ConfigureAwait(false);
        var chapterEnded = next is null || next.Value.ChapterIndex != session.ChapterIndex;
        if (next is not null && _stopTimer.TryConsumeBoundary(chapterEnded))
        {
            session.SetPositionForSave(snapshot.DurationMilliseconds);
            await SaveProgressAsync(
                session,
                session.PositionForSave,
                cancellationToken).ConfigureAwait(false);

            await StartNewSessionAsync(
                next.Value.Book,
                next.Value.ChapterIndex,
                next.Value.SegmentIndex,
                0,
                _currentRule,
                session.SpeakSpeed,
                forceInvalidate: false,
                playImmediately: false,
                pausedState: PlaybackState.Paused,
                pausedMessage: "定时停止已触发。",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (next is null)
        {
            _stopTimer.TryConsumeBoundary(chapterEnded: true);
            session.SetPositionForSave(snapshot.DurationMilliseconds);
            await SaveProgressAsync(
                session,
                session.PositionForSave,
                cancellationToken).ConfigureAwait(false);
            await _prefetchController.CancelAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            await DisposeSessionAsync().ConfigureAwait(false);

            PublishSnapshot(_currentSnapshot with
            {
                State = PlaybackState.Stopped,
                PositionMilliseconds = 0,
                Message = "全书播放完成。",
                CanRetry = false
            });
            return;
        }

        await SaveProgressAsync(
            session,
            snapshot.DurationMilliseconds,
            cancellationToken).ConfigureAwait(false);

        _currentBook = next.Value.Book;
        session.ChapterIndex = next.Value.ChapterIndex;
        session.SegmentIndex = next.Value.SegmentIndex;
        session.ResumePositionMilliseconds = 0;
        await PlayCurrentSegmentAsync(session, 0, forceInvalidate: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessPlaybackFailedAsync(
        PlaybackSessionState session,
        PlaybackErrorEventArgs error,
        LocalAudioPlaybackSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (_currentBook is null)
        {
            return;
        }

        session.UpdateAudio(snapshot);
        if (error.Kind == PlaybackErrorKind.AudioDecode)
        {
            var recoveryKey = $"{session.SessionId:N}:{session.ChapterIndex}:{session.SegmentIndex}:{session.RuleId}:{session.SpeakSpeed}";
            var recoveryDecision = _recoveryPolicy.Decide(new PlaybackRecoveryInput(
                TtsErrorKind.AudioDecode,
                error.Message,
                session.ConsecutiveSegmentFailureCount,
                IsCorruptAudio: true,
                CorruptAudioRecoveryAttempted: string.Equals(
                    _lastRecoveredCorruptSegmentKey,
                    recoveryKey,
                    StringComparison.Ordinal)));
            if (recoveryDecision.ShouldRetryCurrentSegment)
            {
                _lastRecoveredCorruptSegmentKey = recoveryKey;
                await PlayCurrentSegmentAsync(session, 0, forceInvalidate: true, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        PublishPlaybackFailure(
            error.Message,
            TtsErrorKind.AudioDecode);
    }

    private void ProcessSnapshotChanged(PlaybackSessionState session, LocalAudioPlaybackSnapshot snapshot)
    {
        if (_currentBook is null || _currentRule is null)
        {
            return;
        }

        session.UpdateAudio(snapshot);
        if (snapshot.State is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            return;
        }

        PublishSnapshot(BuildSnapshot(
            snapshot.State,
            _currentBook,
            session.ChapterIndex,
            session.SegmentIndex,
            _currentRule,
            session.SpeakSpeed,
            snapshot.PositionMilliseconds,
            snapshot.DurationMilliseconds,
            snapshot.Message,
            snapshot.IsUsingCache,
            false));
    }

    private bool IsCurrentLocalAudioEvent(LocalAudioPlaybackSnapshot snapshot)
    {
        if (_currentSession is null || _currentBook is null)
        {
            return false;
        }

        var current = _localAudioPlaybackCoordinator.CurrentSnapshot;
        return Equals(current, snapshot) &&
            string.Equals(snapshot.BookId, _currentBook.BookId, StringComparison.Ordinal) &&
            snapshot.ChapterIndex == _currentSession.ChapterIndex &&
            snapshot.SegmentIndex == _currentSession.SegmentIndex;
    }

    private void PublishEventCommandFailureSafely()
    {
        if (_disposed)
        {
            return;
        }

        _lastFailureKind = TtsErrorKind.Unknown;
        try
        {
            PublishSnapshot(_currentSnapshot with
            {
                State = PlaybackState.Faulted,
                Message = "播放事件处理失败，请稍后重试。",
                CanRetry = true
            });
        }
        catch (Exception)
        {
            // Snapshot subscribers are outside the event processor's ownership boundary.
            // Do not allow a subscriber failure to become an unobserved task exception.
        }
    }

    private void PublishStopTimerFailureSafely()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            PublishSnapshot(_currentSnapshot with
            {
                Message = "定时停止执行失败，请重新设置。"
            });
        }
        catch
        {
            // Snapshot subscribers are outside the timer task's ownership boundary.
        }
    }

    private enum PlaybackEventCommandKind
    {
        Completed,
        Failed,
        SnapshotChanged
    }

    private sealed record PlaybackEventCommand(
        PlaybackEventCommandKind Kind,
        Guid? SessionId,
        LocalAudioPlaybackSnapshot? Snapshot,
        PlaybackErrorEventArgs? Error)
    {
        public PlaybackEventKey Key => new(
            Kind,
            SessionId ?? Guid.Empty,
            Snapshot?.BookId,
            Snapshot?.ChapterIndex ?? -1,
            Snapshot?.SegmentIndex ?? -1,
            Snapshot?.State ?? PlaybackState.Idle,
            Snapshot?.PositionMilliseconds ?? 0,
            Snapshot?.DurationMilliseconds ?? 0,
            Error?.Kind ?? PlaybackErrorKind.Unknown);
    }

    private readonly record struct PlaybackEventKey(
        PlaybackEventCommandKind Kind,
        Guid SessionId,
        string? BookId,
        int ChapterIndex,
        int SegmentIndex,
        PlaybackState State,
        long PositionMilliseconds,
        long DurationMilliseconds,
        PlaybackErrorKind ErrorKind);

    private static PlaybackSnapshot CreateRuleMissingSnapshot(
        PlaybackBookContent book,
        int chapterIndex,
        int segmentIndex,
        int speakSpeed)
    {
        return PlaybackSnapshotProjector.Project(new PlaybackSnapshotProjectionInput(
            PlaybackState.Stopped,
            book,
            chapterIndex,
            segmentIndex,
            null,
            speakSpeed,
            0,
            0,
            "当前没有可用的 TTS 规则，请先前往规则页选择或导入规则。",
            false,
            false,
            SegmentCountOverride: 0));
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
        bool canRetry)
    {
        return PlaybackSnapshotProjector.Project(new PlaybackSnapshotProjectionInput(
            state,
            book,
            chapterIndex,
            segmentIndex,
            selectedRule,
            speakSpeed,
            positionMilliseconds,
            durationMilliseconds,
            message,
            isUsingCache,
            canRetry,
            _contentRevision));
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

    private async Task<(PlaybackBookContent Book, PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex)?> ResolvePlayablePositionAsync(
        PlaybackBookContent book,
        int? preferredChapterIndex,
        int? preferredSegmentIndex,
        int searchDirection,
        bool preferLastSegmentWhenSearchingBackward,
        CancellationToken cancellationToken)
    {
        foreach (var chapterIndex in PlaybackPositionResolver.GetChapterSearchOrder(
                     book.Chapters,
                     preferredChapterIndex,
                     searchDirection))
        {
            cancellationToken.ThrowIfCancellationRequested();
            book = await EnsureChapterLoadedAsync(book, chapterIndex, cancellationToken);
            var chapter = GetChapter(book, chapterIndex);
            if (chapter is null)
            {
                continue;
            }

            var position = PlaybackPositionResolver.ResolvePlayablePositionInChapter(
                chapter,
                preferredChapterIndex,
                preferredSegmentIndex,
                searchDirection,
                preferLastSegmentWhenSearchingBackward);
            if (position is not null)
            {
                return (book, chapter, position.Value.ChapterIndex, position.Value.SegmentIndex);
            }
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
        var restored = PlaybackPositionResolver.ResolveRestoredPosition(book, progress);
        if (restored is not null && chapter is not null)
        {
            return (
                book,
                chapter,
                restored.Value.ChapterIndex,
                restored.Value.SegmentIndex,
                restored.Value.ResumePositionMilliseconds);
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
        if (chapter is null)
        {
            return null;
        }

        var sameChapterPosition = PlaybackPositionResolver.ResolveRelativeSegmentInChapter(
            chapter,
            segmentIndex,
            delta);
        if (sameChapterPosition is not null)
        {
            return (book, sameChapterPosition.Value.ChapterIndex, sameChapterPosition.Value.SegmentIndex);
        }

        var targetChapterIndex = PlaybackPositionResolver.FindAdjacentChapterIndex(
            book.Chapters,
            chapterIndex,
            delta);
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
        return PlaybackPositionResolver.FindAdjacentChapterIndex(book.Chapters, chapterIndex, delta);
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

    private bool IsSessionCurrent(Guid sessionId)
    {
        return _currentSession is not null && _currentSession.SessionId == sessionId;
    }

    private long GetCurrentPositionMillisecondsForSave(PlaybackSessionState session)
    {
        if (session.HasLoadedAudio)
        {
            var localSnapshot = _localAudioPlaybackCoordinator.CurrentSnapshot;
            if (string.Equals(localSnapshot.BookId, session.BookId, StringComparison.Ordinal) &&
                localSnapshot.ChapterIndex == session.ChapterIndex &&
                localSnapshot.SegmentIndex == session.SegmentIndex)
            {
                return localSnapshot.PositionMilliseconds;
            }
        }

        return session.PositionForSave;
    }

    private Task SaveProgressAsync(
        PlaybackSessionState session,
        long positionMilliseconds,
        CancellationToken cancellationToken)
    {
        if (session.HasLoadedAudio)
        {
            session.UpdateAudio(_localAudioPlaybackCoordinator.CurrentSnapshot);
        }

        session.SetPositionForSave(positionMilliseconds);
        return _progressService.SaveAsync(
            session,
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

    private void ClearCurrentBookContext()
    {
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
            _currentSession?.ReplaceAudioProtection(_audioCacheProtectionRegistry.Protect(filePath));
        }
    }

    private void ClearProtectedPlaybackFile()
    {
        _currentSession?.ReplaceAudioProtection(null);
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
            var savedProgress = await _progressService.RestoreAsync(bookId, cancellationToken).ConfigureAwait(false);
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
            await StartNewSessionAsync(
                book,
                chapterIndex,
                segmentIndex,
                resumePositionMilliseconds,
                selectedRule: null,
                speakSpeed: speakSpeed,
                forceInvalidate: false,
                playImmediately: false,
                pausedState: PlaybackState.Stopped,
                pausedMessage: "当前没有可用的 TTS 规则。",
                cancellationToken).ConfigureAwait(false);
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
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
        await _mutex.WaitAsync(linkedCancellation.Token);
        try
        {
            ThrowIfDisposed();
            await action(linkedCancellation.Token);
        }
        finally
        {
            _mutex.Release();
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
