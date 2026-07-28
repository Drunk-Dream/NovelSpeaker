using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Captures immutable playback inputs and owns their ordered background cache execution.
/// </summary>
public sealed class ActiveCacheCoordinator : IActiveCacheCoordinator, IAsyncDisposable
{
    private const string UnexpectedFailureSummary = "主动缓存失败，请重试。";
    private readonly IBookPlaybackContentService _contentService;
    private readonly ISelectedTtsRuleProvider _ruleProvider;
    private readonly IPlaybackAudioProvider _audioProvider;
    private readonly object _syncRoot = new();
    private ActiveCacheSnapshot? _currentSnapshot;
    private CancellationTokenSource? _activeCancellation;
    private Task? _activeTask;
    private bool _isStarting;
    private bool _disposed;

    public ActiveCacheCoordinator(
        IBookPlaybackContentService contentService,
        ISelectedTtsRuleProvider ruleProvider,
        IPlaybackAudioProvider audioProvider)
    {
        _contentService = contentService;
        _ruleProvider = ruleProvider;
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

            var selectedRule = await _ruleProvider
                .GetSelectedRuleAsync(cancellationToken)
                .ConfigureAwait(false);
            if (selectedRule is null)
            {
                return Rejected(
                    ActiveCacheStartStatus.SelectedRuleUnavailable,
                    "当前没有可用的语音规则。");
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
            foreach (var chapter in selectedChapters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var loaded = await _contentService
                    .GetChapterAsync(book.BookId, chapter.ChapterIndex, cancellationToken)
                    .ConfigureAwait(false);
                if (loaded is null)
                {
                    return Rejected(ActiveCacheStartStatus.NoChaptersSelected, "所选章节无法读取。");
                }

                frozenChapters.Add(new FrozenChapter(
                    loaded.ChapterIndex,
                    loaded.ChapterId,
                    loaded.Title,
                    loaded.Segments
                        .Where(segment => !string.IsNullOrWhiteSpace(segment.SpeechText))
                        .Select(segment => new FrozenSegment(
                            segment.SegmentIndex,
                            segment.StableIdentity,
                            segment.SpeechText))
                        .ToArray()));
            }

            var batchId = Guid.NewGuid();
            var frozenRule = FreezeRule(selectedRule);
            var batch = new FrozenBatch(
                batchId,
                book.BookId,
                book.BookTitle,
                frozenRule,
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

            foreach (var segment in chapter.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PlaybackAudioResult result;
                do
                {
                    result = await _audioProvider.GetAudioAsync(
                        new PlaybackAudioRequest(
                            batch.BookId,
                            chapter.ChapterIndex,
                            segment.SegmentIndex,
                            segment.SpeechText,
                            batch.Rule.RuleId,
                            batch.Rule.SourceRule,
                            batch.Rule.NormalizedRule,
                            batch.SpeakSpeed,
                            batch.BatchId)
                        {
                            ChapterId = chapter.ChapterId,
                            StableSegmentIdentity = segment.StableIdentity
                        },
                        PlaybackAudioPriority.ActiveCache,
                        null,
                        cancellationToken).ConfigureAwait(false);

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
                        Status = ActiveCacheBatchStatus.Failed,
                        ErrorSummary = summary
                    });
                    return;
                }

                snapshot = IncrementSegment(CurrentSnapshot!, chapterPosition);
                Publish(snapshot);
            }

            snapshot = UpdateChapter(
                CurrentSnapshot!,
                chapterPosition,
                ActiveCacheChapterStatus.Completed,
                null,
                currentChapter: true) with
            {
                CompletedChapterCount = CurrentSnapshot!.CompletedChapterCount + 1
            };
            Publish(snapshot);
        }

        Publish(CurrentSnapshot! with
        {
            Status = ActiveCacheBatchStatus.Completed,
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

    private static FrozenRule FreezeRule(SelectedPlaybackRule selectedRule)
    {
        var source = selectedRule.SourceRule with
        {
            Headers = new Dictionary<string, string>(
                selectedRule.SourceRule.Headers,
                StringComparer.OrdinalIgnoreCase)
        };
        var normalized = selectedRule.NormalizedRule with
        {
            UrlTemplate = FreezeTemplate(selectedRule.NormalizedRule.UrlTemplate),
            HeaderTemplates = new Dictionary<string, NormalizedTemplate>(
                selectedRule.NormalizedRule.HeaderTemplates.ToDictionary(
                    pair => pair.Key,
                    pair => FreezeTemplate(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            RequestBodyTemplate = selectedRule.NormalizedRule.RequestBodyTemplate is { } body
                ? FreezeTemplate(body)
                : null
        };
        return new FrozenRule(selectedRule.RuleId, source, normalized);
    }

    private static NormalizedTemplate FreezeTemplate(NormalizedTemplate template) =>
        template with { Segments = template.Segments.ToArray() };

    private static ActiveCacheStartResult Rejected(
        ActiveCacheStartStatus status,
        string summary) =>
        new(status, null, summary);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record FrozenBatch(
        Guid BatchId,
        string BookId,
        string BookTitle,
        FrozenRule Rule,
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

    private sealed record FrozenRule(
        long RuleId,
        HttpTtsRule SourceRule,
        NormalizedHttpTtsRule NormalizedRule);
}
