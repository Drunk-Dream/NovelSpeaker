using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Observability;
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
    IBookRemovalWorkStopper,
    IAsyncDisposable
{
    internal static readonly TimeSpan VolumePersistenceDelay = TimeSpan.FromMilliseconds(300);

    private readonly IBookPlaybackContentService _bookContentService;
    private readonly ICurrentSpeechProvider _selectedProvider;
    private readonly PlaybackSegmentRunner _segmentRunner;
    private readonly PlaybackRecoveryPolicy _recoveryPolicy;
    private readonly IAudioCacheProtectionRegistry _audioCacheProtectionRegistry;
    private readonly PlaybackAudioController _audioController;
    private readonly PlaybackProgressController _progressController;
    private readonly IPlaybackPrefetchController _prefetchController;
    private readonly IAppSettingsService _appSettingsService;
    private readonly TimeProvider _timeProvider;
    private readonly IObservability _observability;
    private readonly PlaybackStopTimer _stopTimer;
    private readonly PlaybackCommandProcessor _commandProcessor;
    private readonly object _disposeGate = new();
    private readonly object _volumePersistenceGate = new();

    private readonly PlaybackRuntime _runtime = new();
    private readonly IBookSourceChangeSource? _sourceChanges;
    private readonly IRegexReplacementRuleWorkspaceService? _regexWorkspace;
    private bool _disposed;
    private Task? _disposeTask;
    private CancellationTokenSource? _volumePersistenceCancellation;
    private Task? _volumePersistenceTask;
    private double _pendingVolume;
    private bool _hasPendingVolumePersistence;

    internal PlaybackCoordinator(
        IBookPlaybackContentService bookContentService,
        ICurrentSpeechProvider selectedProvider,
        PlaybackSegmentRunner segmentRunner,
        PlaybackRecoveryPolicy recoveryPolicy,
        IAudioCacheProtectionRegistry audioCacheProtectionRegistry,
        PlaybackAudioController audioController,
        PlaybackProgressController progressController,
        IPlaybackPrefetchController prefetchController,
        IAppSettingsService appSettingsService,
        TimeProvider timeProvider,
        IObservability? observability = null,
        IBookSourceChangeSource? sourceChanges = null,
        IRegexReplacementRuleWorkspaceService? regexWorkspace = null)
    {
        _bookContentService = bookContentService;
        _selectedProvider = selectedProvider;
        _segmentRunner = segmentRunner;
        _recoveryPolicy = recoveryPolicy;
        _audioCacheProtectionRegistry = audioCacheProtectionRegistry;
        _audioController = audioController;
        _progressController = progressController;
        _prefetchController = prefetchController;
        _appSettingsService = appSettingsService;
        _timeProvider = timeProvider;
        _sourceChanges = sourceChanges;
        _regexWorkspace = regexWorkspace;
        _observability = observability ?? new ObservabilityHub(new ObservabilityContextAccessor());
        _commandProcessor = new PlaybackCommandProcessor(
            ProcessEventCommandAsync,
            PublishEventCommandFailureSafely);
        var startupVolume = PlaybackVolume.Normalize(_appSettingsService.Current.PlaybackVolume);
        _audioController.SetVolume(startupVolume);
        _stopTimer = new PlaybackStopTimer(
            _timeProvider,
            PauseFromTimerAsync,
            PublishStopTimerFailureSafely);

        _audioController.SnapshotChanged += OnLocalSnapshotChanged;
        _audioController.PlaybackCompleted += OnLocalPlaybackCompleted;
        _audioController.PlaybackFailed += OnLocalPlaybackFailed;
        _appSettingsService.Changed += OnSettingsChanged;
        _selectedProvider.Changed += OnProvidersChanged;
        if (_sourceChanges is not null) _sourceChanges.Changed += OnBookCommittedChange;
        if (_regexWorkspace is not null) _regexWorkspace.Changed += OnRegexRulesChanged;
    }

    public PlaybackSnapshot CurrentSnapshot => PlaybackSnapshotProjector.Project(_runtime.Current, _audioController.Volume);

    public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    PlaybackStopTimerSnapshot IPlaybackStopTimer.CurrentSnapshot => _stopTimer.CurrentSnapshot;

    event EventHandler<PlaybackStopTimerSnapshot>? IPlaybackStopTimer.SnapshotChanged
    {
        add => _stopTimer.SnapshotChanged += value;
        remove => _stopTimer.SnapshotChanged -= value;
    }

    void IPlaybackStopTimer.ScheduleAfter(TimeSpan duration) => _stopTimer.ScheduleAfter(duration);

    void IPlaybackStopTimer.Cancel() => _stopTimer.Cancel();

    public async Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = _observability.StartOperation(OperationCatalog.PlaybackStart);
        try
        {
            await RunSerializedAsync(ct => StartCoreAsync(request, ct), cancellationToken).ConfigureAwait(false);
            operation.Complete(OperationResult.Succeeded());
        }
        catch (OperationCanceledException)
        {
            operation.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            operation.Complete(OperationResult.Failed("playback-start-failed"));
            throw;
        }
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

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ClearCoreAsync, cancellationToken);
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

    public Task ChangeProviderAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => ChangeProviderCoreAsync(providerId, ct), cancellationToken);
    }

    public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken)
    {
        return RunSerializedAsync(ct => ChangeSpeedCoreAsync(speakSpeed, ct), cancellationToken);
    }

    public void SetVolume(double volume)
    {
        lock (_volumePersistenceGate)
        {
            ThrowIfDisposed();
            var previous = _audioController.Volume;
            _audioController.SetVolume(volume);
            PublishSnapshot();
            if (_audioController.Volume != previous) ScheduleVolumePersistence(_audioController.Volume);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs change)
    {
        if (change.IsSnapshotReplacement || change.Previous.DefaultSpeakSpeed != change.Current.DefaultSpeakSpeed)
            _commandProcessor.Enqueue(new(PlaybackEventCommandKind.SettingsChanged, Guid.Empty, null, null, 0));
        if (!change.IsSnapshotReplacement && change.Previous.PlaybackVolume == change.Current.PlaybackVolume) return;
        lock (_volumePersistenceGate)
        {
            if (_disposed) return;
            if (!change.IsSnapshotReplacement && _hasPendingVolumePersistence && _pendingVolume != change.Current.PlaybackVolume) return;
            _hasPendingVolumePersistence = false;
            _volumePersistenceCancellation?.Cancel();
            _audioController.SetVolume(change.Current.PlaybackVolume);
            PublishSnapshot();
        }
    }

    private void OnProvidersChanged(object? sender, SpeechProvidersChangedEventArgs change)
    {
        if (!_disposed && change.AffectsSynthesis)
            _commandProcessor.Enqueue(new(PlaybackEventCommandKind.ProviderChanged, Guid.Empty, null, null, 0));
    }

    public Task StopForRemovalAsync(string bookId, string? sourceId, CancellationToken cancellationToken)
    {
        _runtime.CancelWork(bookId, sourceId);
        return _commandProcessor.RunSerializedAsync(async ct =>
        {
            var book = _runtime.Current.Book;
            if (book?.BookId == bookId && (sourceId is null || book.SourceContext?.SourceId == sourceId))
                await DiscardSourceContextAsync(ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    private void OnBookCommittedChange(object? sender, BookCommittedChange change)
    {
        var current = _runtime.Current;
        if (_disposed) return;
        if (change is BookCommittedChange.MetadataCommitted)
        {
            // Metadata facts also apply to a Book whose opening command is still resolving.
            _commandProcessor.Enqueue(new(PlaybackEventCommandKind.BookChanged, Guid.Empty, null, null, 0, change));
            return;
        }
        var preparation = _runtime.ActivePreparation;
        if (preparation is { IsActive: true } && preparation.Book.BookId == change.BookId &&
            InvalidatesBookContext(change, preparation.Book.SourceContext)) preparation.Cancel();
        if (current.Book?.BookId != change.BookId)
        {
            if (preparation is { IsActive: true } && preparation.Book.BookId == change.BookId)
                _commandProcessor.Enqueue(new(PlaybackEventCommandKind.BookChanged, Guid.Empty, null, null, 0, change, preparation.Book.SourceContext));
            return;
        }
        var invalidates = InvalidatesBookContext(change, current.Book.SourceContext);
        if (change is not BookCommittedChange.MetadataCommitted && !invalidates) return;
        if (invalidates) _runtime.CancelSession(current.Identity);
        _commandProcessor.Enqueue(new(PlaybackEventCommandKind.BookChanged,
            current.Identity?.SessionId ?? Guid.Empty, null, null, 0, change, current.Book.SourceContext));
    }

    private static bool InvalidatesBookContext(BookCommittedChange change, ActiveSourceContext? context) => change switch
    {
        BookCommittedChange.ActiveCatalogCommitted catalog => context?.SourceId == catalog.SourceId && context.CatalogVersion != catalog.CatalogVersion,
        BookCommittedChange.ActiveSourceChanged source => context?.SourceId != source.SourceId,
        BookCommittedChange.BookRemoved => true,
        BookCommittedChange.SourceRemoved source => context?.SourceId == source.SourceId,
        _ => false
    };

    private void OnRegexRulesChanged(object? sender, RegexReplacementRulesChangedEventArgs change)
    {
        if (!_disposed && change.AffectsSpeechProfile)
            _commandProcessor.Enqueue(new(PlaybackEventCommandKind.RegexChanged, Guid.Empty, null, null, 0));
    }

    private async Task DiscardSourceContextAsync(CancellationToken cancellationToken)
    {
        _stopTimer.Cancel();
        var hadAudio = _runtime.Current.Audio.HasLoadedAudio;
        var transition = _runtime.Clear(cancellationToken);
        // The Source is already invalid: never checkpoint against its obsolete catalog.
        await ExecuteRetirementAsync(transition, hadAudio).ConfigureAwait(false);
        _runtime.ReportMessage("活动来源目录已更新，请重新打开书籍。");
        PublishSnapshot();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _appSettingsService.Changed -= OnSettingsChanged;
        _selectedProvider.Changed -= OnProvidersChanged;
        if (_sourceChanges is not null) _sourceChanges.Changed -= OnBookCommittedChange;
        if (_regexWorkspace is not null) _regexWorkspace.Changed -= OnRegexRulesChanged;
        await _stopTimer.DisposeAsync().ConfigureAwait(false);
        _commandProcessor.BeginShutdown();
        _runtime.CancelSession(_runtime.Current.Identity);
        _audioController.SnapshotChanged -= OnLocalSnapshotChanged;
        _audioController.PlaybackCompleted -= OnLocalPlaybackCompleted;
        _audioController.PlaybackFailed -= OnLocalPlaybackFailed;
        Exception? failure = null;
        async Task ObserveAsync(Func<Task> effect)
        {
            try { await effect().ConfigureAwait(false); }
            catch (Exception exception) { failure ??= exception; }
        }
        await ObserveAsync(FlushVolumePersistenceAsync).ConfigureAwait(false);
        await _commandProcessor.WaitForIdleAsync().ConfigureAwait(false);
        CaptureDevicePosition();
        var hadAudio = _runtime.Current.Audio.HasLoadedAudio;
        var stop = _runtime.Stop("已停止当前播放。");
        await ObserveAsync(() => ExecuteCheckpointsAsync(stop, CancellationToken.None)).ConfigureAwait(false);
        await ObserveAsync(() => ExecuteRetirementAsync(stop, hadAudio)).ConfigureAwait(false);
        _runtime.Dispose();
        await ObserveAsync(() => _commandProcessor.DisposeAsync().AsTask()).ConfigureAwait(false);
        await ObserveAsync(() => _audioController.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private Task StartCoreAsync(PlaybackStartRequest request, CancellationToken ct) =>
        OpenBookCoreAsync(request.BookId, request.ChapterIndex, request.SegmentIndex,
            request.ResumePositionMilliseconds, request.SpeakSpeedOverride, true, ct);

    private Task OpenPausedCoreAsync(OpenBookPlaybackRequest request, CancellationToken ct) =>
        OpenBookCoreAsync(request.BookId, request.ChapterIndex, request.SegmentIndex,
            null, request.SpeakSpeedOverride, false, ct);

    private async Task OpenBookCoreAsync(string bookId, int? chapterIndex, int? segmentIndex,
        long? resume, int? speed, bool play, CancellationToken cancellationToken)
    {
        using var work = _runtime.BeginPreparation(new(bookId, string.Empty, []));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, work.Token);
        cancellationToken = linked.Token;
        var resolved = await ResolveBookStartContextAsync(bookId, chapterIndex, segmentIndex, resume, true, cancellationToken, work).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolved is null)
        {
            if (_runtime.Current.Book is null)
            {
                _runtime.ReportFailure("未找到要打开的书籍。");
                PublishSnapshot();
            }
            return;
        }
        var provider = await _selectedProvider.GetSelectedProviderAsync(cancellationToken).ConfigureAwait(false);
        await ReplaceSessionAsync(new(resolved.Value.Book, new(resolved.Value.ChapterIndex, resolved.Value.SegmentIndex),
            provider, speed ?? _appSettingsService.Current.DefaultSpeakSpeed,
            play && provider is not null ? PlaybackState.Preparing : provider is null ? PlaybackState.Stopped : PlaybackState.Paused,
            resolved.Value.ResumePositionMilliseconds, Message: provider is null ? ProviderMissingMessage : "已恢复到当前位置，等待播放。"),
            chapterIndex is not null || segmentIndex is not null || resolved.Value.RequiresCheckpoint,
            false, cancellationToken).ConfigureAwait(false);
    }

    private const string ProviderMissingMessage = "尚未选择语音服务，请前往语音服务管理完成配置。";

    private async Task PauseCoreAsync(CancellationToken cancellationToken)
    {
        var current = _runtime.Current;
        if (current.Identity is null) return;
        if (current.Audio.HasLoadedAudio)
        {
            await _audioController.PauseAsync(cancellationToken).ConfigureAwait(false);
            AcceptDeviceSnapshot(_audioController.CurrentSnapshot);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var transition = _runtime.Pause();
        PublishSnapshot();
        await ExecuteCheckpointsAsync(transition, cancellationToken).ConfigureAwait(false);
        await RefreshPrefetchWindowAsync(_runtime.Current, 1, cancellationToken).ConfigureAwait(false);
    }

    private Task PauseFromTimerAsync(CancellationToken cancellationToken) => RunSerializedAsync(async ct =>
    {
        try { await PauseCoreAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // Observe the timer's pause failure before releasing the command boundary.
            // A queued completion may otherwise replace the session and cancel the timer
            // before its task can report the original checkpoint failure.
            _runtime.ReportMessage("定时停止执行失败，请重新设置。");
            try { PublishSnapshot(); } catch { }
        }
    }, cancellationToken);

    private async Task ResumeCoreAsync(CancellationToken cancellationToken)
    {
        var current = _runtime.Current;
        if (current.Book is null || current.Position is null) return;
        if (current.Audio.HasLoadedAudio)
        {
            await _audioController.ResumeAsync(cancellationToken).ConfigureAwait(false);
            AcceptDeviceSnapshot(_audioController.CurrentSnapshot);
            _runtime.ResetFailureWindow();
            PublishSnapshot();
            await RefreshPrefetchWindowAsync(_runtime.Current, null, cancellationToken).ConfigureAwait(false);
            return;
        }
        using var work = _runtime.BeginPreparation(current.Book);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, work.Token);
        cancellationToken = linked.Token;
        var provider = await _selectedProvider.GetSelectedProviderAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (provider is null)
        {
            _runtime.ChangeSpeechConfiguration(current.Identity, null, current.SpeakSpeed);
            _runtime.ReportMessage(ProviderMissingMessage);
            PublishSnapshot();
            return;
        }
        await ReplaceSessionAsync(new(current.Book, current.Position, provider, current.SpeakSpeed,
            PlaybackState.Preparing, current.ResumePositionMilliseconds), false, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        _stopTimer.Cancel();
        if (_runtime.Current.Book is null) return;
        CaptureDevicePosition();
        // A checkpoint failure leaves the current session available for another attempt.
        await SaveCurrentAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var hadAudio = _runtime.Current.Audio.HasLoadedAudio;
        var stop = _runtime.Stop("已停止当前播放。");
        await ExecuteRetirementAsync(stop, hadAudio).ConfigureAwait(false);
        PublishSnapshot();
    }

    private async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        _stopTimer.Cancel();
        CaptureDevicePosition();
        await SaveCurrentAsync(cancellationToken).ConfigureAwait(false);
        var hadAudio = _runtime.Current.Audio.HasLoadedAudio;
        var clear = _runtime.Clear(cancellationToken);
        await ExecuteRetirementAsync(clear, hadAudio).ConfigureAwait(false);
        PublishSnapshot();
    }

    private async Task RefreshBookMetadataCoreAsync(string bookId, CancellationToken cancellationToken)
    {
        var current = _runtime.Current;
        if (current.Book?.BookId != bookId) return;
        var book = await _bookContentService.GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (book is null) return;
        if (book.SourceContext != current.Book.SourceContext)
        {
            await DiscardSourceContextAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        _runtime.UpdateMetadata(book);
        PublishSnapshot();
    }

    private async Task RefreshRegexReplacementCoreAsync(CancellationToken cancellationToken)
    {
        var current = _runtime.Current;
        if (current.Book is null || current.Position is not { } position) return;
        using var work = _runtime.BeginPreparation(current.Book);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runtime.SessionToken, work.Token);
        var token = linked.Token;
        var previous = GetChapter(current.Book, position.ChapterIndex)!.Segments[position.SegmentIndex];
        var chapter = await _bookContentService.GetChapterAsync(current.Book.BookId, position.ChapterIndex, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (chapter is null) return;
        await ValidateBookAsync(current.Book, token).ConfigureAwait(false);
        var book = ReplaceChapter(current.Book, chapter);
        var mapped = PlaybackPositionResolver.FindMappedSegmentIndex(chapter, previous.StartOffset);
        if (mapped >= 0)
        {
            var segment = chapter.Segments[mapped];
            if (previous.StableIdentity == segment.StableIdentity && previous.SpeechText == segment.SpeechText)
            {
                _runtime.UpdateContent(book, new(position.ChapterIndex, mapped), "正则替换规则已应用。");
                if (current.Identity is { } identity)
                    await _prefetchController.CancelAsync(identity.SessionId, token).ConfigureAwait(false);
                await RefreshPrefetchWindowAsync(_runtime.Current, null, token).ConfigureAwait(false);
                PublishSnapshot();
                return;
            }
        }
        var target = mapped >= 0 ? (Book: book, ChapterIndex: position.ChapterIndex, SegmentIndex: mapped) :
            await FindNearestAsync(book, position.ChapterIndex, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var provider = await _selectedProvider.GetSelectedProviderAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (target is null)
        {
            // The old logical position no longer belongs to the new speech plan.
            CaptureDevicePosition();
            var preparation = _runtime.PrepareReplacement(new(book, null, provider, current.SpeakSpeed,
                PlaybackState.Stopped, Message: "正则替换后没有可播放的段落。"));
            var transition = _runtime.CommitReplacement(preparation.Replacement!, token, false);
            await ExecuteReplacementEffectsAsync(transition, current.Audio.HasLoadedAudio, null, null, cancellationToken).ConfigureAwait(false);
            return;
        }
        await ReplaceSessionAsync(new(target.Value.Book, new(target.Value.ChapterIndex, target.Value.SegmentIndex),
            provider, current.SpeakSpeed,
            provider is null || current.State == PlaybackState.Stopped ? PlaybackState.Stopped :
                current.State == PlaybackState.Playing ? PlaybackState.Preparing : PlaybackState.Paused,
            Message: provider is null ? ProviderMissingMessage : "正则替换规则已应用，等待播放。"), true, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(PlaybackBookContent Book, int ChapterIndex, int SegmentIndex)?> FindNearestAsync(
        PlaybackBookContent book, int chapter, CancellationToken ct)
    {
        var target = await ResolveNearestAvailablePositionAsync(book, chapter, ct).ConfigureAwait(false);
        return target is null ? null : (target.Value.Book, target.Value.ChapterIndex, target.Value.SegmentIndex);
    }

    private async Task JumpToCoreAsync(int chapterIndex, int segmentIndex, CancellationToken ct)
    {
        var current = _runtime.Current;
        if (current.Book is null) return;
        using var work = _runtime.BeginPreparation(current.Book);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        ct = linked.Token;
        var target = await ResolvePlayablePositionAsync(current.Book, chapterIndex, segmentIndex, 1, false, ct).ConfigureAwait(false);
        if (target is not null) await NavigateAsync(target.Value.Book, target.Value.ChapterIndex, target.Value.SegmentIndex, ct).ConfigureAwait(false);
    }

    private Task JumpToChapterCoreAsync(int chapterIndex, CancellationToken ct) => JumpToCoreAsync(chapterIndex, 0, ct);

    private async Task MoveSegmentCoreAsync(int delta, CancellationToken ct)
    {
        var current = _runtime.Current;
        if (current.Book is null || current.Position is not { } position) return;
        using var work = _runtime.BeginPreparation(current.Book);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        ct = linked.Token;
        var target = await ResolveRelativeSegmentAsync(current.Book, position.ChapterIndex, position.SegmentIndex, delta, ct).ConfigureAwait(false);
        if (target is not null) await NavigateAsync(target.Value.Book, target.Value.ChapterIndex, target.Value.SegmentIndex, ct).ConfigureAwait(false);
    }

    private async Task MoveChapterCoreAsync(int delta, CancellationToken ct)
    {
        using var operation = _observability.StartOperation(OperationCatalog.PlaybackChapterSwitch);
        try
        {
            var current = _runtime.Current;
            if (current.Book is not null && current.Position is { } position &&
                FindRelativeChapterIndex(current.Book, position.ChapterIndex, delta) is { } chapter)
            {
                using var work = _runtime.BeginPreparation(current.Book);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
                ct = linked.Token;
                var target = await ResolvePlayablePositionAsync(current.Book, chapter, 0, delta >= 0 ? 1 : -1, false, ct).ConfigureAwait(false);
                if (target is not null) await NavigateAsync(target.Value.Book, target.Value.ChapterIndex, target.Value.SegmentIndex, ct).ConfigureAwait(false);
            }
            operation.Complete(OperationResult.Succeeded());
        }
        catch (OperationCanceledException) { operation.Complete(OperationResult.Cancelled()); throw; }
        catch { operation.Complete(OperationResult.Failed("chapter-switch-failed")); throw; }
    }

    private async Task NavigateAsync(PlaybackBookContent book, int chapter, int segment, CancellationToken ct)
    {
        var current = _runtime.Current;
        var provider = await _selectedProvider.GetSelectedProviderAsync(ct).ConfigureAwait(false);
        await ReplaceSessionAsync(new(book, new(chapter, segment), provider, current.SpeakSpeed,
            current.State == PlaybackState.Playing && provider is not null ? PlaybackState.Preparing :
                provider is null ? PlaybackState.Stopped : PlaybackState.Paused,
            Message: provider is null ? ProviderMissingMessage : "已跳转到目标段落，等待播放。"), true, false, ct).ConfigureAwait(false);
    }

    private async Task RetryCurrentSegmentCoreAsync(CancellationToken ct)
    {
        var current = _runtime.Current;
        if (current.Book is null || current.Position is null) return;
        using var work = _runtime.BeginPreparation(current.Book);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        ct = linked.Token;
        var provider = await _selectedProvider.GetSelectedProviderAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (provider is null) { _runtime.ReportMessage(ProviderMissingMessage); PublishSnapshot(); return; }
        await ReplaceSessionAsync(new(current.Book, current.Position, provider, current.SpeakSpeed, PlaybackState.Preparing),
            false, current.LastFailureKind == TtsErrorKind.AudioDecode, ct).ConfigureAwait(false);
    }

    private async Task ChangeProviderCoreAsync(ProviderId providerId, CancellationToken ct)
    {
        var provider = await _selectedProvider.SelectProviderAsync(providerId, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (provider is null) return;
        await ApplyProviderAsync(provider, ct).ConfigureAwait(false);
    }

    private async Task ApplyProviderAsync(ResolvedSpeechProvider? provider, CancellationToken ct)
    {
        var current = _runtime.Current;
        if (!current.Audio.HasLoadedAudio) _runtime.ChangeSpeechConfiguration(current.Identity, provider, current.SpeakSpeed);
        PublishSnapshot();
        await RefreshPrefetchWindowAsync(_runtime.Current, null, ct).ConfigureAwait(false);
    }

    private async Task ChangeSpeedCoreAsync(int speed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = _runtime.Current;
        if (current.Identity is { } identity) _runtime.ChangeSpeechConfiguration(identity, current.Provider, speed);
        else _runtime.ChangeIdleSpeed(speed);
        PublishSnapshot();
        await RefreshPrefetchWindowAsync(_runtime.Current, null, ct).ConfigureAwait(false);
    }

    private async Task ReplaceSessionAsync(PlaybackSessionTarget target, bool checkpoint, bool forceInvalidate, CancellationToken ct,
        bool cancelStopTimer = true)
    {
        using var work = _runtime.BeginPreparation(target.Book);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        var preparationToken = linked.Token;
        // Preparation can read content/provider or generate audio, but cannot stop the old
        // device, checkpoint the target, or release its cache protection.
        await ValidateBookAsync(target.Book, preparationToken).ConfigureAwait(false);
        var requestedState = target.State;
        while (true)
        {
            var provider = await _selectedProvider.GetSelectedProviderAsync(preparationToken).ConfigureAwait(false);
            target = target with
            {
                Provider = provider,
                State = provider is null && requestedState == PlaybackState.Preparing ? PlaybackState.Stopped : requestedState,
                Message = provider is null ? ProviderMissingMessage : target.Message
            };
            var preparation = _runtime.PrepareReplacement(target);
            if (!preparation.IsAccepted) throw new InvalidOperationException("无效的播放目标。");
            PlaybackSegmentRunRequest? request = null;
            AudioGenerationResult? audio = null;
            if (target.State == PlaybackState.Preparing)
            {
                request = CreateSegmentRequest(preparation.Replacement!.State, forceInvalidate);
                audio = await _segmentRunner.PrepareAsync(request, null, preparationToken).ConfigureAwait(false);
                preparationToken.ThrowIfCancellationRequested();
                if (audio.Failure?.Kind == TtsErrorKind.Cancelled) throw new OperationCanceledException(ct);
            }
            await ValidateBookAsync(target.Book, preparationToken).ConfigureAwait(false);
            var latestProvider = await _selectedProvider.GetSelectedProviderAsync(preparationToken).ConfigureAwait(false);
            preparationToken.ThrowIfCancellationRequested();
            // A sentence has not started during synthesis. Reprepare on a committed
            // selection/configuration change rather than playing its obsolete audio.
            if (!HasSameSynthesisProvider(provider, latestProvider)) continue;
            var hadAudio = _runtime.Current.Audio.HasLoadedAudio;
            var device = _audioController.CurrentSnapshot;
            var retiringPosition = MatchesDevice(_runtime.Current, device) && device.State is PlaybackState.Playing or PlaybackState.Paused
                ? device.PositionMilliseconds : _runtime.Current.PositionForSave;
            var transition = _runtime.CommitReplacement(preparation.Replacement!, preparationToken, checkpoint, retiringPosition);
            if (!transition.IsAccepted) throw new OperationCanceledException(ct);
            if (cancelStopTimer) _stopTimer.Cancel();
            _commandProcessor.AdvanceEventEpoch();
            await ExecuteReplacementEffectsAsync(transition, hadAudio, request, audio, ct).ConfigureAwait(false);
            return;
        }
    }

    private static bool HasSameSynthesisProvider(ResolvedSpeechProvider? first, ResolvedSpeechProvider? second) =>
        first is null ? second is null : second is not null && first.ProviderId == second.ProviderId &&
            ProviderSynthesisFingerprint.Create(first.Provider).Equals(ProviderSynthesisFingerprint.Create(second.Provider));

    private async Task ExecuteReplacementEffectsAsync(PlaybackTransition transition, bool hadAudio,
        PlaybackSegmentRunRequest? request, AudioGenerationResult? audio, CancellationToken ct)
    {
        // Retirement cannot be cancelled halfway by the caller after the replacement is
        // committed. Checkpoint/device failures never roll back a committed runtime.
        Exception? effectFailure = null;
        try { await ExecuteRetirementAsync(transition, hadAudio).ConfigureAwait(false); }
        catch (Exception exception) { effectFailure = exception; }
        try { await ExecuteCheckpointsAsync(transition, ct).ConfigureAwait(false); }
        catch (Exception exception) { effectFailure ??= exception; }
        PublishSnapshot();
        if (effectFailure is not null)
        {
            if (transition.State.Identity is { } identity && transition.State.Position is { } position)
                _runtime.AcceptAudio(new(identity, position, PlaybackState.Stopped, PlaybackAudioFacts.Empty,
                    "播放切换未完成，已保留当前目标。"));
            else _runtime.ReportMessage("播放切换未完成，已保留当前目标。");
            PublishSnapshot();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(effectFailure).Throw();
        }
        try
        {
            if (request is not null && audio is not null)
                await PlayPreparedSegmentAsync(transition.State, request, audio, ct).ConfigureAwait(false);
            else await RefreshPrefetchWindowAsync(_runtime.Current, 1, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (transition.State.Identity is { } identity && _runtime.IsCurrent(identity))
            {
                _runtime.Pause();
                PublishSnapshot();
            }
            throw;
        }
        catch
        {
            if (transition.State.Identity is { } identity && _runtime.IsCurrent(identity))
            {
                _runtime.ReportFailure("播放准备失败，请重试。");
                PublishSnapshot();
            }
            throw;
        }
    }

    private async Task ExecuteRetirementAsync(PlaybackTransition transition, bool stopAudio)
    {
        Exception? failure = null;
        foreach (var effect in transition.Effects.OfType<PlaybackRetireSessionEffect>())
        {
            effect.Lifetime.Cancel();
            try
            {
                if (stopAudio) await _audioController.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) { failure ??= exception; }
            try { await _prefetchController.CancelAsync(effect.Lifetime.Identity.SessionId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { failure ??= exception; }
            finally
            {
                try { effect.Lifetime.Dispose(); }
                catch (Exception exception) { failure ??= exception; }
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private Task SaveCurrentAsync(CancellationToken ct)
    {
        if (_runtime.Current.Identity is not { } identity) return Task.CompletedTask;
        return ExecuteCheckpointsAsync(_runtime.Checkpoint(identity), ct);
    }

    private async Task ExecuteCheckpointsAsync(PlaybackTransition transition, CancellationToken ct)
    {
        foreach (var effect in transition.Effects.OfType<PlaybackCheckpointEffect>())
            await _progressController.SaveAsync(effect.Progress, ct).ConfigureAwait(false);
    }

    private PlaybackSegmentRunRequest CreateSegmentRequest(PlaybackRuntimeState state, bool forceInvalidate)
    {
        var position = state.Position!.Value;
        var chapter = GetChapter(state.Book!, position.ChapterIndex)!;
        var segment = chapter.Segments[position.SegmentIndex];
        return new(new AudioGenerationRequest(state.Book!.BookId, position.ChapterIndex, position.SegmentIndex,
            segment.SpeechText, state.Provider!, state.SpeakSpeed, state.Identity!.SessionId)
        { ChapterId = chapter.ChapterId, StableSegmentIdentity = segment.StableIdentity },
            $"{state.Book.BookTitle} · {chapter.Title}", state.ResumePositionMilliseconds, forceInvalidate,
            token => ValidateBookAsync(state.Book, token));
    }

    private async Task PlayPreparedSegmentAsync(PlaybackRuntimeState state, PlaybackSegmentRunRequest request,
        AudioGenerationResult audio, CancellationToken ct)
    {
        if (!_runtime.IsCurrent(state.Identity!)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _runtime.SessionToken);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        while (true)
        {
            var provider = await _selectedProvider.GetSelectedProviderAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!_runtime.IsCurrent(state.Identity!)) return;
            if (HasSameSynthesisProvider(request.AudioRequest.Provider, provider)) break;
            // Retirement/checkpoint and corrupt-audio preparation also await effects.
            // Consume any committed configuration before this sentence starts.
            _runtime.ChangeSpeechConfiguration(state.Identity, provider, state.SpeakSpeed);
            if (provider is null)
            {
                _runtime.AcceptAudio(new(state.Identity!, state.Position!.Value, PlaybackState.Stopped,
                    PlaybackAudioFacts.Empty, ProviderMissingMessage));
                PublishSnapshot();
                await RefreshPrefetchWindowAsync(_runtime.Current, null, token).ConfigureAwait(false);
                return;
            }
            state = _runtime.Current;
            request = CreateSegmentRequest(state, request.ForceInvalidate);
            audio = await _segmentRunner.PrepareAsync(request, null, token).ConfigureAwait(false);
        }
        if (!audio.IsSuccess)
        {
            await HandleSegmentFailureAsync(audio.Failure!, false, ct).ConfigureAwait(false);
            return;
        }
        _runtime.BeginPlayback(false, request.ForceInvalidate);
        PublishSnapshot();
        var protection = _audioCacheProtectionRegistry.Protect(audio.FilePath!);
        if (!_runtime.TryProtectAudio(state.Identity!, protection)) { protection.Dispose(); return; }
        var run = await _segmentRunner.PlayPreparedAsync(request, audio, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!_runtime.IsCurrent(state.Identity!)) return;
        AcceptDeviceSnapshot(run.LocalSnapshot);
        PublishSnapshot();
        await RefreshPrefetchWindowAsync(_runtime.Current, null, token).ConfigureAwait(false);
    }

    private async Task HandleSegmentFailureAsync(TtsExecutionFailure failure, bool corruptAudio, CancellationToken ct)
    {
        var current = _runtime.Current;
        if (current.Identity is null || !_runtime.IsCurrent(current.Identity)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _runtime.SessionToken);
        var token = linked.Token;
        var decision = _runtime.RecordFailure(_recoveryPolicy, failure.Kind, failure.Message, corruptAudio);
        if (decision.ShouldRetryCurrentSegment)
        {
            if (current.Audio.HasLoadedAudio)
            {
                await _audioController.StopAsync(token).ConfigureAwait(false);
                AcceptDeviceSnapshot(_audioController.CurrentSnapshot);
            }
            token.ThrowIfCancellationRequested();
            _runtime.BeginPlayback(false, recovering: true);
            PublishSnapshot();
            var request = CreateSegmentRequest(_runtime.Current, true);
            var audio = await _segmentRunner.PrepareAsync(request, null, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await PlayPreparedSegmentAsync(_runtime.Current, request, audio, ct).ConfigureAwait(false);
            return;
        }
        if (!decision.ShouldSkipCurrentSegment) return;
        var position = current.Position!.Value;
        var next = await ResolveRelativeSegmentAsync(current.Book!, position.ChapterIndex, position.SegmentIndex, 1, token).ConfigureAwait(false);
        if (next is null)
        {
            await FinishAsync("当前段播放失败，已跳过并结束播放。", ct).ConfigureAwait(false);
            return;
        }
        var provider = await _selectedProvider.GetSelectedProviderAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await ReplaceSessionAsync(new(next.Value.Book, new(next.Value.ChapterIndex, next.Value.SegmentIndex), provider,
            current.SpeakSpeed, !decision.ShouldPause && provider is not null ? PlaybackState.Preparing : PlaybackState.Paused,
            ConsecutiveSegmentFailureCount: decision.ConsecutiveSegmentFailureCount,
            Message: decision.Message, CanRetry: decision.ShouldPause), true, false, ct, cancelStopTimer: false).ConfigureAwait(false);
    }

    private async Task RefreshPrefetchWindowAsync(PlaybackRuntimeState state, int? maxCount, CancellationToken ct)
    {
        if (state.Identity is null || state.Book is null || state.Position is not { } position || !_runtime.IsCurrent(state.Identity)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _runtime.SessionToken);
        var token = linked.Token;
        var requests = new List<AudioGenerationRequest>();
        var provider = await _selectedProvider.GetSelectedProviderAsync(token).ConfigureAwait(false);
        var count = Math.Clamp(_appSettingsService.Current.PrefetchCount, 0, AppSettings.DefaultPrefetchCountValue);
        if (maxCount is not null) count = Math.Min(count, maxCount.Value);
        var book = state.Book;
        for (var i = 0; provider is not null && i < count; i++)
        {
            var next = await ResolveRelativeSegmentAsync(book, position.ChapterIndex, position.SegmentIndex, 1, token).ConfigureAwait(false);
            if (next is null) break;
            book = next.Value.Book;
            position = new(next.Value.ChapterIndex, next.Value.SegmentIndex);
            var chapter = GetChapter(book, position.ChapterIndex)!;
            var segment = chapter.Segments[position.SegmentIndex];
            requests.Add(new(book.BookId, position.ChapterIndex, position.SegmentIndex, segment.SpeechText,
                provider, state.SpeakSpeed, state.Identity.SessionId)
            { ChapterId = chapter.ChapterId, StableSegmentIdentity = segment.StableIdentity });
        }
        await ValidateBookAsync(book, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!_runtime.IsCurrent(state.Identity)) return;
        if (!ReferenceEquals(book, state.Book)) _runtime.UpdateContent(book, state.Position);
        await _prefetchController.SubmitAsync(new(state.Identity.SessionId, requests), token).ConfigureAwait(false);
    }

    private async Task ValidateBookAsync(PlaybackBookContent book, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await _bookContentService.IsCurrentAsync(book, ct).ConfigureAwait(false))
            throw new OperationCanceledException("活动来源目录已更新。", ct);
        ct.ThrowIfCancellationRequested();
    }

    // Audio callback wiring and epoch checks remain for the T006 effect/result cleanup.
    // Their accepted data enters the same runtime; no session or snapshot mirror exists.
    private void OnLocalPlaybackCompleted(object? sender, EventArgs e) => EnqueueAudioEvent(PlaybackEventCommandKind.Completed, _audioController.CurrentSnapshot);
    private void OnLocalPlaybackFailed(object? sender, PlaybackErrorEventArgs error) => EnqueueAudioEvent(PlaybackEventCommandKind.Failed, _audioController.CurrentSnapshot, error);
    private void OnLocalSnapshotChanged(object? sender, LocalAudioPlaybackSnapshot snapshot) => EnqueueAudioEvent(PlaybackEventCommandKind.SnapshotChanged, snapshot);

    private void EnqueueAudioEvent(PlaybackEventCommandKind kind, LocalAudioPlaybackSnapshot snapshot, PlaybackErrorEventArgs? error = null) =>
        _commandProcessor.Enqueue(new(kind, snapshot.PlaybackSessionId ?? _runtime.Current.Identity?.SessionId,
            snapshot, error, _commandProcessor.CurrentEventEpoch));

    private async Task ProcessEventCommandAsync(PlaybackEventCommand command, CancellationToken ct)
    {
        if (_disposed) return;
        if (command.Kind is PlaybackEventCommandKind.StopTimerFailed or PlaybackEventCommandKind.EventProcessingFailed)
        {
            if (command.Kind == PlaybackEventCommandKind.StopTimerFailed) _runtime.ReportMessage("定时停止执行失败，请重新设置。");
            else _runtime.ReportFailure("播放事件处理失败，请稍后重试。");
            try { PublishSnapshot(); } catch { }
            return;
        }
        var sessionToken = _runtime.SessionToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, sessionToken);
        try
        {
            if (command.Kind == PlaybackEventCommandKind.BookChanged)
            {
                if (command.BookChange?.BookId != _runtime.Current.Book?.BookId)
                {
                    if (_runtime.Current.Book is null && command.BookChange is not BookCommittedChange.MetadataCommitted)
                        await DiscardSourceContextAsync(ct).ConfigureAwait(false);
                    return;
                }
                if (command.BookChange is BookCommittedChange.MetadataCommitted metadata)
                    await RefreshBookMetadataCoreAsync(metadata.BookId, ct).ConfigureAwait(false);
                else if (command.BookChange is { } change && command.SourceContext == _runtime.Current.Book?.SourceContext &&
                    InvalidatesBookContext(change, _runtime.Current.Book?.SourceContext))
                    await DiscardSourceContextAsync(ct).ConfigureAwait(false);
                return;
            }
            if (command.Kind == PlaybackEventCommandKind.SettingsChanged)
            {
                await ChangeSpeedCoreAsync(_appSettingsService.Current.DefaultSpeakSpeed, ct).ConfigureAwait(false);
                return;
            }
            if (command.Kind == PlaybackEventCommandKind.ProviderChanged)
            {
                await ApplyProviderAsync(await _selectedProvider.GetSelectedProviderAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                return;
            }
            if (command.Kind == PlaybackEventCommandKind.RegexChanged)
            {
                await RefreshRegexReplacementCoreAsync(ct).ConfigureAwait(false);
                return;
            }
            var current = _runtime.Current;
            if (current.Identity is not { } identity || !_runtime.IsCurrent(identity) || command.SessionId != identity.SessionId ||
                command.EventEpoch != _commandProcessor.CurrentEventEpoch || command.Snapshot is not { } snapshot ||
                snapshot != _audioController.CurrentSnapshot || !MatchesDevice(current, snapshot)) return;
            if (command.Kind == PlaybackEventCommandKind.SnapshotChanged)
            {
                if (snapshot.State is PlaybackState.Playing or PlaybackState.Paused) { AcceptDeviceSnapshot(snapshot); PublishSnapshot(); }
                return;
            }
            await ValidateBookAsync(current.Book!, linked.Token).ConfigureAwait(false);
            AcceptDeviceSnapshot(snapshot);
            if (command.Kind == PlaybackEventCommandKind.Failed)
            {
                await HandleSegmentFailureAsync(new(TtsErrorKind.AudioDecode, command.Error!.Message, null, null, null, null),
                    command.Error.Kind is PlaybackErrorKind.AudioDecode or PlaybackErrorKind.UnsupportedFormat, ct).ConfigureAwait(false);
                return;
            }
            _runtime.ResetFailureWindow();
            var position = current.Position!.Value;
            _runtime.CaptureCheckpointPosition(identity, snapshot.DurationMilliseconds);
            var next = await ResolveRelativeSegmentAsync(current.Book!, position.ChapterIndex, position.SegmentIndex, 1, linked.Token).ConfigureAwait(false);
            if (next is null) { await FinishAsync("全书播放完成。", ct).ConfigureAwait(false); return; }
            var provider = await _selectedProvider.GetSelectedProviderAsync(ct).ConfigureAwait(false);
            await ReplaceSessionAsync(new(next.Value.Book, new(next.Value.ChapterIndex, next.Value.SegmentIndex), provider,
                current.SpeakSpeed, provider is null ? PlaybackState.Stopped : PlaybackState.Preparing,
                Message: provider is null ? ProviderMissingMessage : null), true, false, ct, cancelStopTimer: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (ct.IsCancellationRequested || sessionToken.IsCancellationRequested ||
            exception.CancellationToken.IsCancellationRequested)
        { }
    }

    private async Task FinishAsync(string message, CancellationToken ct)
    {
        var stop = _runtime.Stop(message);
        Exception? failure = null;
        try { await ExecuteCheckpointsAsync(stop, ct).ConfigureAwait(false); }
        catch (Exception exception) { failure = exception; }
        try { await ExecuteRetirementAsync(stop, false).ConfigureAwait(false); }
        catch (Exception exception) { failure ??= exception; }
        PublishSnapshot();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static bool MatchesDevice(PlaybackRuntimeState state, LocalAudioPlaybackSnapshot snapshot) =>
        state.Identity is not null && snapshot.PlaybackSessionId == state.Identity.SessionId &&
        snapshot.BookId == state.Book?.BookId;

    private void CaptureDevicePosition()
    {
        var state = _runtime.Current;
        var snapshot = _audioController.CurrentSnapshot;
        if (MatchesDevice(state, snapshot))
            _runtime.CaptureCheckpointPosition(state.Identity!, snapshot.State is PlaybackState.Stopped or PlaybackState.Faulted
                ? state.PositionForSave : snapshot.PositionMilliseconds);
    }

    private void AcceptDeviceSnapshot(LocalAudioPlaybackSnapshot snapshot)
    {
        var current = _runtime.Current;
        if (!MatchesDevice(current, snapshot) || current.Position is not { } position) return;
        _runtime.AcceptAudio(new(current.Identity!, position, snapshot.State,
            new(snapshot.State is PlaybackState.Playing or PlaybackState.Paused, snapshot.PositionMilliseconds,
                snapshot.DurationMilliseconds, snapshot.IsUsingCache),
            snapshot.Message ?? (snapshot.State == PlaybackState.Paused && current.State == PlaybackState.Paused ? current.Message : null)));
    }

    private void PublishEventCommandFailureSafely()
    {
        if (_disposed) return;
        _commandProcessor.Enqueue(new(PlaybackEventCommandKind.EventProcessingFailed, Guid.Empty, null, null, 0));
    }

    private void PublishStopTimerFailureSafely()
    {
        if (_disposed) return;
        _commandProcessor.Enqueue(new(PlaybackEventCommandKind.StopTimerFailed, Guid.Empty, null, null, 0));
    }

    private Task RunSerializedAsync(Func<CancellationToken, Task> action, CancellationToken ct) =>
        _commandProcessor.RunSerializedAsync(async token =>
        {
            ThrowIfDisposed();
            if (_runtime.Current.Book is { } book && !await _bookContentService.IsCurrentAsync(book, token).ConfigureAwait(false))
                await DiscardSourceContextAsync(token).ConfigureAwait(false);
            await action(token).ConfigureAwait(false);
        }, ct);

    private void PublishSnapshot() => SnapshotChanged?.Invoke(this, CurrentSnapshot);
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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
        if (book.Chapters.Count == 0) return null;
        var chapterIndex = Math.Clamp(progress.ChapterIndex, book.Chapters.Min(c => c.ChapterIndex), book.Chapters.Max(c => c.ChapterIndex));
        progress = progress with
        {
            ChapterIndex = chapterIndex,
            AudioPositionMilliseconds = chapterIndex == progress.ChapterIndex ? progress.AudioPositionMilliseconds : 0
        };
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
        if (!await _bookContentService.IsCurrentAsync(book, cancellationToken).ConfigureAwait(false) ||
            (existing.ChapterId is not null && loadedChapter?.ChapterId != existing.ChapterId))
            throw new OperationCanceledException("活动来源目录已更新。", cancellationToken);
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

    private async Task<(PlaybackBookContent Book, PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex, long ResumePositionMilliseconds, bool RequiresCheckpoint)?> ResolveBookStartContextAsync(
        string bookId,
        int? requestedChapterIndex,
        int? requestedSegmentIndex,
        long? resumePositionMillisecondsOverride,
        bool allowSavedProgress,
        CancellationToken cancellationToken,
        PlaybackPreparationLifetime preparation)
    {
        var book = await _bookContentService.GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        preparation.SetBook(book);
        cancellationToken.ThrowIfCancellationRequested();

        var requiresCheckpoint = false;
        var hasExplicitPosition = requestedChapterIndex is not null || requestedSegmentIndex is not null;
        var resumePositionMilliseconds = resumePositionMillisecondsOverride ?? 0;
        (PlaybackBookContent Book, PlaybackChapterContent Chapter, int ChapterIndex, int SegmentIndex)? startPosition = null;

        if (allowSavedProgress && !hasExplicitPosition)
        {
            var savedProgress = await _progressController.RestoreAsync(bookId, cancellationToken).ConfigureAwait(false);
            if (savedProgress is not null)
            {
                var restoredPosition = await ResolveRestoredPositionAsync(book, savedProgress, cancellationToken).ConfigureAwait(false);
                if (restoredPosition is not null)
                {
                    book = restoredPosition.Value.Book;
                    requiresCheckpoint = savedProgress.ChapterIndex != restoredPosition.Value.ChapterIndex ||
                        savedProgress.SegmentIndex != restoredPosition.Value.SegmentIndex ||
                        savedProgress.CharacterOffset != restoredPosition.Value.Chapter.Segments[restoredPosition.Value.SegmentIndex].StartOffset ||
                        savedProgress.AudioPositionMilliseconds != restoredPosition.Value.ResumePositionMilliseconds;
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
            resumePositionMilliseconds,
            requiresCheckpoint);
    }

    private void ScheduleVolumePersistence(double volume)
    {
        CancellationTokenSource persistenceCancellation;
        Task persistenceTask;
        lock (_volumePersistenceGate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingVolume = volume;
            _hasPendingVolumePersistence = true;
            _volumePersistenceCancellation?.Cancel();
            persistenceCancellation = CancellationTokenSource.CreateLinkedTokenSource(_commandProcessor.LifecycleToken);
            _volumePersistenceCancellation = persistenceCancellation;
            persistenceTask = PersistVolumeAfterDelayAsync(persistenceCancellation);
            _volumePersistenceTask = persistenceTask;
        }
    }

    private async Task PersistVolumeAfterDelayAsync(CancellationTokenSource persistenceCancellation)
    {
        try
        {
            await Task.Delay(
                VolumePersistenceDelay,
                _timeProvider,
                persistenceCancellation.Token).ConfigureAwait(false);
            await PersistPendingVolumeAsync(persistenceCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (persistenceCancellation.IsCancellationRequested)
        {
            // Replacing a newer slider value or closing the application cancels the debounce task.
        }
        catch (Exception)
        {
            // Settings persistence is best effort while playback remains usable. The pending value is
            // retained and a later slider change or shutdown flush retries it.
        }
        finally
        {
            lock (_volumePersistenceGate)
            {
                if (ReferenceEquals(_volumePersistenceCancellation, persistenceCancellation))
                {
                    _volumePersistenceCancellation = null;
                    _volumePersistenceTask = null;
                }
            }

            persistenceCancellation.Dispose();
        }
    }

    private async Task FlushVolumePersistenceAsync()
    {
        Task? persistenceTask;
        lock (_volumePersistenceGate)
        {
            _volumePersistenceCancellation?.Cancel();
            persistenceTask = _volumePersistenceTask;
        }

        if (persistenceTask is not null)
        {
            await persistenceTask.ConfigureAwait(false);
        }

        await PersistPendingVolumeAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task PersistPendingVolumeAsync(CancellationToken cancellationToken)
    {
        double volume;
        lock (_volumePersistenceGate)
        {
            if (!_hasPendingVolumePersistence)
            {
                return;
            }

            volume = _pendingVolume;
        }

        await _appSettingsService.UpdateAsync(
            new AppSettingsUpdate { PlaybackVolume = volume },
            cancellationToken).ConfigureAwait(false);

        lock (_volumePersistenceGate)
        {
            if (_hasPendingVolumePersistence && _pendingVolume == volume)
            {
                _hasPendingVolumePersistence = false;
            }
        }
    }

}
