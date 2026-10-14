using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Application.Speech.Execution;

namespace NovelSpeaker.Application.Cache.ActiveCache;

/// <summary>
/// Captures immutable playback inputs and owns their ordered background cache execution.
/// </summary>
public sealed class ActiveCacheCoordinator : IActiveCacheCoordinator, IActiveCacheLifetime, IAsyncDisposable
{
    private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(5);
    private const string UnexpectedFailureSummary = "主动缓存失败，请重试。";
    private readonly IBookPlaybackContentService _contentService;
    private readonly ICurrentSpeechProvider _providers;
    private readonly IAudioGenerationProvider _audioProvider;
    private readonly BookMutationGate _mutations;
    private readonly object _syncRoot = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private TaskCompletionSource? _startingCompletion;
    private bool _stopping;
    private ActiveCacheSnapshot? _currentSnapshot;
    private CancellationTokenSource? _activeCancellation;
    private Task? _activeTask;
    private bool _isStarting;
    private bool _disposed;
    private string? _activeSourceId;

    public ActiveCacheCoordinator(
        IBookPlaybackContentService contentService,
        ICurrentSpeechProvider providers,
        IAudioGenerationProvider audioProvider, BookMutationGate mutations)
    {
        _contentService = contentService;
        _providers = providers;
        _audioProvider = audioProvider;
        _mutations = mutations;
    }

    public ActiveCacheSnapshot? CurrentSnapshot => Volatile.Read(ref _currentSnapshot);

    public event EventHandler<ActiveCacheSnapshot>? SnapshotChanged;

    public async Task<ActiveCacheStartResult> StartAsync(
        StartActiveCacheRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BookId);
        ArgumentNullException.ThrowIfNull(request.ChapterIndices);
        CancellationTokenSource startCancellation;
        TaskCompletionSource startingCompletion;
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_isStarting || _activeTask is { IsCompleted: false })
            {
                return new ActiveCacheStartResult(
                    ActiveCacheStartStatus.BatchAlreadyActive,
                    null,
                    "已有主动缓存批次正在运行。");
            }

            _isStarting = true;
            startingCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _startingCompletion = startingCompletion;
            startCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetimeCancellation.Token);
        }

        using var ownedStartCancellation = startCancellation;
        cancellationToken = startCancellation.Token;
        try
        {
            return await _mutations.RunAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.ChapterIndices.Count == 0)
                {
                    return Rejected(ActiveCacheStartStatus.NoChaptersSelected, "请至少选择一个章节。");
                }

                var provider = await _providers
                    .GetSelectedProviderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (provider is null)
                {
                    return Rejected(
                        ActiveCacheStartStatus.SelectedProviderUnavailable,
                        "尚未选择语音服务。");
                }
                var frozenProvider = provider with { Provider = CurrentSpeechProvider.Snapshot(provider.Provider) };

                var book = await _contentService
                    .GetBookAsync(request.BookId, cancellationToken)
                    .ConfigureAwait(false);
                if (book is null)
                {
                    return Rejected(ActiveCacheStartStatus.BookNotFound, "书籍不存在或已被删除。");
                }

                var requestedIndices = request.ChapterIndices.ToHashSet();
                var selectedChapters = book.Chapters
                    .Where(chapter => requestedIndices.Contains(chapter.ChapterIndex))
                    .ToArray();
                if (selectedChapters.Length == 0)
                {
                    return Rejected(ActiveCacheStartStatus.NoChaptersSelected, "所选章节不存在。");
                }

                var chapterReader = await _contentService.FreezeChapterReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await _contentService.IsCurrentAsync(book, cancellationToken).ConfigureAwait(false))
                    return Rejected(ActiveCacheStartStatus.NoChaptersSelected, "活动来源目录已更新，请重新选择章节。");
                var frozenBook = book with
                {
                    Chapters = selectedChapters.Select(chapter => PlaybackChapterContent.Unloaded(
                        chapter.ChapterIndex, chapter.Title, chapter.ChapterId)).ToArray()
                };

                var batchId = Guid.NewGuid();
                var batch = new FrozenBatch(
                    batchId,
                    frozenBook,
                    frozenProvider,
                    AppSettings.NormalizeSpeakSpeed(request.SpeakSpeed),
                    chapterReader);
                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationTokenSource batchCancellation;

                lock (_syncRoot)
                {
                    ThrowIfDisposed();
                    cancellationToken.ThrowIfCancellationRequested();
                    batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                    _activeCancellation = batchCancellation;
                    _activeTask = completion.Task;
                    _activeSourceId = book.SourceContext?.SourceId;
                }

                Publish(CreateInitialSnapshot(batch));
                _ = Task.Run(() => RunOwnedBatchAsync(batch, batchCancellation, completion));
                return new ActiveCacheStartResult(ActiveCacheStartStatus.Accepted, batchId, null);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_syncRoot)
            {
                _isStarting = false;
                startingCompletion.TrySetResult();
            }
        }
    }

    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Task? activeTask;
        lock (_syncRoot)
        {
            activeTask = _activeTask;
            var activeCancellation = _activeCancellation;
            if (activeTask is null || activeTask.IsCompleted || activeCancellation is null)
            {
                return;
            }

            // Keep the active-slot check and cancellation together so a replacement
            // batch cannot dispose this token source before it is cancelled.
            activeCancellation.Cancel();

            var snapshot = CurrentSnapshot;
            if (snapshot is not null &&
                snapshot.Status is ActiveCacheBatchStatus.Waiting or ActiveCacheBatchStatus.Running)
            {
                Publish(snapshot with { Status = ActiveCacheBatchStatus.Cancelling });
            }
        }

        await activeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopForRemovalAsync(string bookId, string? sourceId, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            if (CurrentSnapshot?.BookId != bookId || (sourceId is not null && _activeSourceId != sourceId))
                return Task.CompletedTask;
        }
        return CancelAsync(cancellationToken);
    }

    public async Task WaitForCurrentBatchAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Task? activeTask;
        lock (_syncRoot)
        {
            activeTask = _activeTask;
        }

        if (activeTask is not null)
        {
            await activeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task stopTask;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            stopTask = StopAsync(CancellationToken.None);
        }

        try
        {
            await stopTask.WaitAsync(DisposeWaitTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The process drain records the timeout. A late worker still owns its CTS;
            // disposal cannot declare that worker complete or release resources it uses.
        }

        _lifetimeCancellation.Dispose();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            if (!_stopping)
            {
                _stopping = true;
                _lifetimeCancellation.Cancel();
                var snapshot = CurrentSnapshot;
                if (_activeTask is { IsCompleted: false } && snapshot is not null &&
                    snapshot.Status is ActiveCacheBatchStatus.Waiting or ActiveCacheBatchStatus.Running)
                {
                    Publish(snapshot with { Status = ActiveCacheBatchStatus.Cancelling });
                }
            }

            return Task.WhenAll(_startingCompletion?.Task ?? Task.CompletedTask,
                _activeTask ?? Task.CompletedTask).WaitAsync(cancellationToken);
        }
    }

    private async Task RunOwnedBatchAsync(
        FrozenBatch batch,
        CancellationTokenSource batchCancellation,
        TaskCompletionSource completion)
    {
        try
        {
            await RunBatchAsync(batch, batchCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PublishCancelled();
        }
        catch (Exception)
        {
            PublishFailed(UnexpectedFailureSummary);
        }
        finally
        {
            completion.TrySetResult();
            lock (_syncRoot)
            {
                if (ReferenceEquals(_activeCancellation, batchCancellation))
                    _activeCancellation = null;
                batchCancellation.Dispose();
            }
        }
    }

    private async Task RunBatchAsync(FrozenBatch batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = CurrentSnapshot! with { Status = ActiveCacheBatchStatus.Running };
        Publish(snapshot);

        for (var chapterPosition = 0; chapterPosition < batch.Book.Chapters.Count; chapterPosition++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunChapterAsync(batch, chapterPosition, cancellationToken).ConfigureAwait(false);
        }

        Publish(CurrentSnapshot! with
        {
            Status = CurrentSnapshot!.Chapters.Any(chapter => chapter.Status == ActiveCacheChapterStatus.Failed)
                ? ActiveCacheBatchStatus.Failed : ActiveCacheBatchStatus.Completed,
            CurrentChapterIndex = null,
            CurrentChapterTitle = null
        });
    }

    private async Task RunChapterAsync(FrozenBatch batch, int chapterPosition, CancellationToken cancellationToken)
    {
        var chapter = batch.Book.Chapters[chapterPosition];
        Publish(UpdateChapter(CurrentSnapshot!, chapterPosition, ActiveCacheChapterStatus.Running,
            null, currentChapter: true));
        if (!await _contentService.IsCurrentAsync(batch.Book, cancellationToken).ConfigureAwait(false))
            throw new OperationCanceledException("活动来源目录已更新。", cancellationToken);
        var loaded = await batch.ReadChapter(batch.Book.BookId, chapter.ChapterIndex, cancellationToken).ConfigureAwait(false);
        if (loaded is null || (chapter.ChapterId is not null && loaded.ChapterId != chapter.ChapterId) ||
            !await _contentService.IsCurrentAsync(batch.Book, cancellationToken).ConfigureAwait(false))
            throw new OperationCanceledException("活动来源目录已更新。", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var chapters = CurrentSnapshot!.Chapters.ToArray();
        var count = loaded.Segments.Count(segment => NarratableText.HasContent(segment.SpeechText));
        chapters[chapterPosition] = chapters[chapterPosition] with { TotalSegmentCount = count };
        var snapshot = CurrentSnapshot with
        {
            Chapters = chapters,
            TotalSegmentCount = CurrentSnapshot.TotalSegmentCount + count
        };
        Publish(snapshot);

        var usedOnlyCache = true;
        var chapterFailed = false;
        foreach (var segment in loaded.Segments.Where(segment => NarratableText.HasContent(segment.SpeechText)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _contentService.IsCurrentAsync(batch.Book, cancellationToken).ConfigureAwait(false))
                throw new OperationCanceledException("活动来源目录已更新。", cancellationToken);
            AudioGenerationResult result;
            do
            {
                try
                {
                    result = await _audioProvider.GetAudioAsync(
                        new AudioGenerationRequest(
                            batch.Book.BookId,
                            chapter.ChapterIndex,
                            segment.SegmentIndex,
                            segment.SpeechText,
                            batch.Provider,
                            batch.SpeakSpeed,
                            batch.BatchId)
                        {
                            ChapterId = chapter.ChapterId,
                            SpeechPlan = loaded.SpeechPlan,
                            StableSegmentIdentity = segment.StableIdentity
                        },
                        AudioGenerationPriority.ActiveCache,
                        null,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    result = new AudioGenerationResult(null, false,
                        new TtsExecutionFailure(TtsErrorKind.Unknown, UnexpectedFailureSummary, null, null, null, null));
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!await _contentService.IsCurrentAsync(batch.Book, cancellationToken).ConfigureAwait(false))
                    throw new OperationCanceledException("活动来源目录已更新。", cancellationToken);
            }
            while (result.Failure?.Kind == TtsErrorKind.Cancelled);

            if (!result.IsSuccess)
            {
                var summary = result.Failure?.Message ?? UnexpectedFailureSummary;
                Publish(UpdateChapter(
                    CurrentSnapshot!,
                    chapterPosition,
                    ActiveCacheChapterStatus.Failed,
                    summary,
                    currentChapter: true) with
                {
                    ErrorSummary = summary
                });
                chapterFailed = true;
                break;
            }

            usedOnlyCache &= result.IsUsingCache;
            snapshot = IncrementSegment(CurrentSnapshot!, chapterPosition);
            Publish(snapshot);
        }

        snapshot = UpdateChapter(
            CurrentSnapshot!,
            chapterPosition,
            chapterFailed ? ActiveCacheChapterStatus.Failed : usedOnlyCache ? ActiveCacheChapterStatus.Skipped : ActiveCacheChapterStatus.Completed,
            chapterFailed ? CurrentSnapshot!.Chapters[chapterPosition].ErrorSummary : null,
            currentChapter: true) with
        {
            CompletedChapterCount = CurrentSnapshot!.CompletedChapterCount + 1
        };
        Publish(snapshot);
    }

    private void PublishCancelled()
    {
        var snapshot = CurrentSnapshot;
        if (snapshot is null)
        {
            return;
        }

        var chapters = snapshot.Chapters
            .Select(chapter => chapter.Status == ActiveCacheChapterStatus.Running
                ? chapter with { Status = ActiveCacheChapterStatus.Cancelled }
                : chapter)
            .ToArray();
        Publish(snapshot with
        {
            Status = ActiveCacheBatchStatus.Cancelled,
            Chapters = chapters,
            ErrorSummary = null
        });
    }

    private void PublishFailed(string summary)
    {
        var snapshot = CurrentSnapshot;
        if (snapshot is null)
        {
            return;
        }

        var chapters = snapshot.Chapters
            .Select(chapter => chapter.Status == ActiveCacheChapterStatus.Running
                ? chapter with { Status = ActiveCacheChapterStatus.Failed, ErrorSummary = summary }
                : chapter)
            .ToArray();
        Publish(snapshot with
        {
            Status = ActiveCacheBatchStatus.Failed,
            Chapters = chapters,
            ErrorSummary = summary
        });
    }

    private void Publish(ActiveCacheSnapshot snapshot)
    {
        Volatile.Write(ref _currentSnapshot, snapshot);
        foreach (EventHandler<ActiveCacheSnapshot> handler in SnapshotChanged?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception)
            {
                // UI observers cannot interrupt the batch owner or other observers.
            }
        }
    }

    private static ActiveCacheSnapshot CreateInitialSnapshot(FrozenBatch batch)
    {
        var chapters = batch.Book.Chapters
            .Select(chapter => new ActiveCacheChapterSnapshot(
                chapter.ChapterIndex,
                chapter.Title,
                0,
                0,
                ActiveCacheChapterStatus.Pending,
                null))
            .ToArray();
        return new ActiveCacheSnapshot(
            batch.BatchId,
            batch.Book.BookId,
            batch.Book.BookTitle,
            ActiveCacheBatchStatus.Waiting,
            0,
            chapters.Length,
            0,
            chapters.Sum(chapter => chapter.TotalSegmentCount),
            null,
            null,
            chapters,
            null);
    }

    private static ActiveCacheSnapshot IncrementSegment(ActiveCacheSnapshot snapshot, int chapterPosition)
    {
        var chapters = snapshot.Chapters.ToArray();
        chapters[chapterPosition] = chapters[chapterPosition] with
        {
            CompletedSegmentCount = chapters[chapterPosition].CompletedSegmentCount + 1
        };
        return snapshot with
        {
            CompletedSegmentCount = snapshot.CompletedSegmentCount + 1,
            Chapters = chapters
        };
    }

    private static ActiveCacheSnapshot UpdateChapter(
        ActiveCacheSnapshot snapshot,
        int chapterPosition,
        ActiveCacheChapterStatus status,
        string? errorSummary,
        bool currentChapter)
    {
        var chapters = snapshot.Chapters.ToArray();
        chapters[chapterPosition] = chapters[chapterPosition] with
        {
            Status = status,
            ErrorSummary = errorSummary
        };
        return snapshot with
        {
            Chapters = chapters,
            CurrentChapterIndex = currentChapter ? chapters[chapterPosition].ChapterIndex : null,
            CurrentChapterTitle = currentChapter ? chapters[chapterPosition].ChapterTitle : null
        };
    }

    private static ActiveCacheStartResult Rejected(ActiveCacheStartStatus status, string summary) => new(status, null, summary);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record FrozenBatch(
        Guid BatchId,
        PlaybackBookContent Book,
        ResolvedSpeechProvider Provider,
        int SpeakSpeed,
        ChapterContentReader ReadChapter);
}
