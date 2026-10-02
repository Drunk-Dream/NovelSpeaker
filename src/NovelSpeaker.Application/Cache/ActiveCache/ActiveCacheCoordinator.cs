using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Application.Speech.Execution;

namespace NovelSpeaker.Application.Cache.ActiveCache;

/// <summary>
/// Captures immutable playback inputs and owns their ordered background cache execution.
/// </summary>
public sealed class ActiveCacheCoordinator : IActiveCacheCoordinator, IAsyncDisposable
{
    private const string UnexpectedFailureSummary = "主动缓存失败，请重试。";
    private readonly IBookPlaybackContentService _contentService;
    private readonly ICurrentSpeechProvider _providers;
    private readonly IAudioGenerationProvider _audioProvider;
    private readonly object _syncRoot = new();
    private ActiveCacheSnapshot? _currentSnapshot;
    private CancellationTokenSource? _activeCancellation;
    private Task? _activeTask;
    private bool _isStarting;
    private bool _disposed;

    public ActiveCacheCoordinator(
        IBookPlaybackContentService contentService,
        ICurrentSpeechProvider providers,
        IAudioGenerationProvider audioProvider)
    {
        _contentService = contentService;
        _providers = providers;
        _audioProvider = audioProvider;
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
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (_isStarting || _activeTask is { IsCompleted: false })
            {
                return new ActiveCacheStartResult(
                    ActiveCacheStartStatus.BatchAlreadyActive,
                    null,
                    "已有主动缓存批次正在运行。");
            }

            _isStarting = true;
        }

        try
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

            var frozenChapters = new List<FrozenChapter>(selectedChapters.Length);
            var loadedChapters = await _contentService.GetChaptersAsync(book.BookId,
                selectedChapters.Select(chapter => chapter.ChapterIndex).ToArray(), cancellationToken).ConfigureAwait(false);
            var loadedByIndex = loadedChapters.ToDictionary(chapter => chapter.ChapterIndex);
            foreach (var chapter in selectedChapters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!loadedByIndex.TryGetValue(chapter.ChapterIndex, out var loaded))
                {
                    return Rejected(ActiveCacheStartStatus.NoChaptersSelected, "所选章节无法读取。");
                }

                frozenChapters.Add(new FrozenChapter(
                    loaded.ChapterIndex,
                    loaded.ChapterId,
                    loaded.Title,
                    loaded.Segments
                        .Where(segment => NarratableText.HasContent(segment.SpeechText))
                        .Select(segment => new FrozenSegment(
                            segment.SegmentIndex,
                            segment.StableIdentity,
                            segment.SpeechText))
                        .ToArray()));
            }

            var batchId = Guid.NewGuid();
            var frozenProvider = provider with { Provider = CurrentSpeechProvider.Snapshot(provider.Provider) };
            var batch = new FrozenBatch(
                batchId,
                book.BookId,
                book.BookTitle,
                frozenProvider,
                AppSettings.NormalizeSpeakSpeed(request.SpeakSpeed),
                frozenChapters);
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenSource batchCancellation;

            lock (_syncRoot)
            {
                ThrowIfDisposed();
                _activeCancellation?.Dispose();
                batchCancellation = new CancellationTokenSource();
                _activeCancellation = batchCancellation;
                _activeTask = completion.Task;
                _isStarting = false;
            }

            Publish(CreateInitialSnapshot(batch));
            _ = RunOwnedBatchAsync(batch, batchCancellation, completion);
            return new ActiveCacheStartResult(ActiveCacheStartStatus.Accepted, batchId, null);
        }
        finally
        {
            lock (_syncRoot)
            {
                _isStarting = false;
            }
        }
    }

    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Task? activeTask;
        CancellationTokenSource? activeCancellation;
        lock (_syncRoot)
        {
            activeTask = _activeTask;
            activeCancellation = _activeCancellation;
        }

        if (activeTask is null || activeTask.IsCompleted || activeCancellation is null)
        {
            return;
        }

        var snapshot = CurrentSnapshot;
        if (snapshot is not null &&
            snapshot.Status is ActiveCacheBatchStatus.Waiting or ActiveCacheBatchStatus.Running)
        {
            Publish(snapshot with { Status = ActiveCacheBatchStatus.Cancelling });
        }

        activeCancellation.Cancel();
        await activeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        Task? activeTask;
        CancellationTokenSource? activeCancellation;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            activeTask = _activeTask;
            activeCancellation = _activeCancellation;
        }

        activeCancellation?.Cancel();
        if (activeTask is not null)
        {
            // Container shutdown owns this final, deliberately non-cancellable drain.
            await activeTask.ConfigureAwait(false);
        }

        activeCancellation?.Dispose();
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
        catch (OperationCanceledException) when (batchCancellation.IsCancellationRequested)
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
        }
    }

    private async Task RunBatchAsync(FrozenBatch batch, CancellationToken cancellationToken)
    {
        var snapshot = CurrentSnapshot! with { Status = ActiveCacheBatchStatus.Running };
        Publish(snapshot);

        for (var chapterPosition = 0; chapterPosition < batch.Chapters.Count; chapterPosition++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapter = batch.Chapters[chapterPosition];
            snapshot = UpdateChapter(
                CurrentSnapshot!,
                chapterPosition,
                ActiveCacheChapterStatus.Running,
                null,
                currentChapter: true);
            Publish(snapshot);

            var usedOnlyCache = true;
            var chapterFailed = false;
            foreach (var segment in chapter.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AudioGenerationResult result;
                do
                {
                    try
                    {
                        result = await _audioProvider.GetAudioAsync(
                            new AudioGenerationRequest(
                                batch.BookId,
                                chapter.ChapterIndex,
                                segment.SegmentIndex,
                                segment.SpeechText,
                                batch.Provider,
                                batch.SpeakSpeed,
                                batch.BatchId)
                            {
                                ChapterId = chapter.ChapterId,
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

        Publish(CurrentSnapshot! with
        {
            Status = CurrentSnapshot!.Chapters.Any(chapter => chapter.Status == ActiveCacheChapterStatus.Failed)
                ? ActiveCacheBatchStatus.Failed : ActiveCacheBatchStatus.Completed,
            CurrentChapterIndex = null,
            CurrentChapterTitle = null
        });
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
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static ActiveCacheSnapshot CreateInitialSnapshot(FrozenBatch batch)
    {
        var chapters = batch.Chapters
            .Select(chapter => new ActiveCacheChapterSnapshot(
                chapter.ChapterIndex,
                chapter.Title,
                0,
                chapter.Segments.Count,
                ActiveCacheChapterStatus.Pending,
                null))
            .ToArray();
        return new ActiveCacheSnapshot(
            batch.BatchId,
            batch.BookId,
            batch.BookTitle,
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
        string BookId,
        string BookTitle,
        ResolvedSpeechProvider Provider,
        int SpeakSpeed,
        IReadOnlyList<FrozenChapter> Chapters);

    private sealed record FrozenChapter(
        int ChapterIndex,
        string? ChapterId,
        string Title,
        IReadOnlyList<FrozenSegment> Segments);

    private sealed record FrozenSegment(
        int SegmentIndex,
        StableSpeechSegmentIdentity StableIdentity,
        string SpeechText);

}
