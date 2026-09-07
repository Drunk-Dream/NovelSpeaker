namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Coalesces high-frequency Cache mutations without becoming a general message bus.
/// </summary>
public sealed class CacheInvalidationCoordinator : ICacheInvalidationCoordinator
{
    private static readonly TimeSpan CoalescingWindow = TimeSpan.FromMilliseconds(50);
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private List<CacheInvalidation> _pending = [];
    private Task? _scheduledFlush;
    private bool _stopping;

    public CacheInvalidationCoordinator(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<CacheInvalidationBatch>? BatchPublished;

    public void Publish(CacheInvalidation invalidation)
    {
        ArgumentNullException.ThrowIfNull(invalidation);

        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _pending.Add(invalidation);
            _scheduledFlush ??= FlushAfterWindowAsync();
        }
    }

    public async Task FlushPendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CacheInvalidationBatch? batch;
            lock (_gate)
            {
                batch = TakePendingBatch();
            }

            if (batch is not null)
            {
                PublishBatch(batch);
            }
        }
        finally
        {
            _publishGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? scheduledFlush;
        lock (_gate)
        {
            if (_stopping)
            {
                scheduledFlush = _scheduledFlush;
            }
            else
            {
                _stopping = true;
                scheduledFlush = _scheduledFlush;
                _lifetimeCancellation.Cancel();
            }
        }

        if (scheduledFlush is not null)
        {
            await scheduledFlush.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await FlushPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Disposal has no external cancellation contract.
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _publishGate.Dispose();
        }
    }

    private async Task FlushAfterWindowAsync()
    {
        try
        {
            await Task.Delay(CoalescingWindow, _timeProvider, _lifetimeCancellation.Token)
                .ConfigureAwait(false);
            await FlushPendingAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // StopAsync drains pending work after cancelling the scheduled delay.
        }
        finally
        {
            lock (_gate)
            {
                _scheduledFlush = null;
                if (!_stopping && _pending.Count > 0)
                {
                    _scheduledFlush = FlushAfterWindowAsync();
                }
            }
        }
    }

    private CacheInvalidationBatch? TakePendingBatch()
    {
        if (_pending.Count == 0)
        {
            return null;
        }

        var pending = _pending;
        _pending = [];
        return new CacheInvalidationBatch(Coalesce(pending));
    }

    private void PublishBatch(CacheInvalidationBatch batch)
    {
        foreach (EventHandler<CacheInvalidationBatch> handler in BatchPublished?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, batch);
            }
            catch
            {
                // A consumer must not fault the process-scoped invalidation owner.
            }
        }
    }

    private static IReadOnlyList<CacheInvalidation> Coalesce(
        IReadOnlyCollection<CacheInvalidation> changes)
    {
        var merged = new List<CacheInvalidation>();
        foreach (var change in changes)
        {
            switch (change.Scope)
            {
                case CacheInvalidationScope.Global:
                    MergeGlobal(merged, change.Aspects);
                    break;
                case CacheInvalidationScope.Book book:
                    MergeBook(merged, book.BookId, change.Aspects);
                    break;
                case CacheInvalidationScope.Chapters chapters:
                    MergeChapters(merged, chapters, change.Aspects);
                    break;
            }
        }

        return merged;
    }

    private static void MergeGlobal(
        List<CacheInvalidation> merged,
        CacheInvalidationAspect aspects)
    {
        var globalIndex = FindGlobal(merged);
        if (globalIndex >= 0)
        {
            var global = merged[globalIndex];
            merged[globalIndex] = global with { Aspects = global.Aspects | aspects };
        }
        else
        {
            merged.Add(CacheInvalidation.ForGlobal(aspects));
        }

        globalIndex = FindGlobal(merged);
        var coveredAspects = merged[globalIndex].Aspects;
        for (var index = merged.Count - 1; index >= 0; index--)
        {
            if (index == globalIndex || merged[index].Scope is CacheInvalidationScope.Global)
            {
                continue;
            }

            var remainingAspects = merged[index].Aspects & ~coveredAspects;
            if (remainingAspects == CacheInvalidationAspect.None)
            {
                merged.RemoveAt(index);
            }
            else if (merged[index].Scope is CacheInvalidationScope.Book book)
            {
                merged[index] = CacheInvalidation.ForBook(book.BookId, remainingAspects);
            }
            else if (merged[index].Scope is CacheInvalidationScope.Chapters chapters)
            {
                merged[index] = CacheInvalidation.ForChapters(
                    chapters.BookId,
                    chapters.ChapterIndices,
                    remainingAspects);
            }
        }
    }

    private static void MergeBook(
        List<CacheInvalidation> merged,
        string bookId,
        CacheInvalidationAspect aspects)
    {
        var globalAspects = GetGlobalAspects(merged);
        aspects &= ~globalAspects;
        if (aspects == CacheInvalidationAspect.None)
        {
            return;
        }

        var index = merged.FindIndex(change =>
            change.Scope is CacheInvalidationScope.Book book &&
            string.Equals(book.BookId, bookId, StringComparison.Ordinal));
        if (index >= 0)
        {
            merged[index] = merged[index] with { Aspects = merged[index].Aspects | aspects };
        }
        else
        {
            merged.Add(CacheInvalidation.ForBook(bookId, aspects));
        }

        RemoveCoveredChapterScopes(merged, bookId, aspects);
    }

    private static void MergeChapters(
        List<CacheInvalidation> merged,
        CacheInvalidationScope.Chapters chapters,
        CacheInvalidationAspect aspects)
    {
        var globalAspects = GetGlobalAspects(merged);
        var bookAspects = GetBookAspects(merged, chapters.BookId);
        aspects &= ~(globalAspects | bookAspects);
        if (aspects == CacheInvalidationAspect.None)
        {
            return;
        }

        var index = merged.FindIndex(change =>
            change.Scope is CacheInvalidationScope.Chapters existing &&
            string.Equals(existing.BookId, chapters.BookId, StringComparison.Ordinal));
        if (index < 0)
        {
            merged.Add(CacheInvalidation.ForChapters(chapters.BookId, chapters.ChapterIndices, aspects));
            return;
        }

        var existingScope = (CacheInvalidationScope.Chapters)merged[index].Scope;
        merged[index] = CacheInvalidation.ForChapters(
            chapters.BookId,
            existingScope.ChapterIndices.Concat(chapters.ChapterIndices),
            merged[index].Aspects | aspects);
    }

    private static void RemoveCoveredChapterScopes(
        List<CacheInvalidation> merged,
        string bookId,
        CacheInvalidationAspect coveringAspects)
    {
        for (var index = merged.Count - 1; index >= 0; index--)
        {
            if (merged[index].Scope is not CacheInvalidationScope.Chapters chapters ||
                !string.Equals(chapters.BookId, bookId, StringComparison.Ordinal))
            {
                continue;
            }

            var remainingAspects = merged[index].Aspects & ~coveringAspects;
            if (remainingAspects == CacheInvalidationAspect.None)
            {
                merged.RemoveAt(index);
            }
            else
            {
                merged[index] = CacheInvalidation.ForChapters(
                    bookId,
                    chapters.ChapterIndices,
                    remainingAspects);
            }
        }
    }

    private static CacheInvalidationAspect GetBookAspects(
        IReadOnlyCollection<CacheInvalidation> changes,
        string bookId) =>
        changes
            .Where(change => change.Scope is CacheInvalidationScope.Book book &&
                             string.Equals(book.BookId, bookId, StringComparison.Ordinal))
            .Aggregate(
                CacheInvalidationAspect.None,
                static (current, change) => current | change.Aspects);

    private static CacheInvalidationAspect GetGlobalAspects(
        IReadOnlyCollection<CacheInvalidation> changes) =>
        changes
            .Where(change => change.Scope is CacheInvalidationScope.Global)
            .Aggregate(
                CacheInvalidationAspect.None,
                static (current, change) => current | change.Aspects);

    private static int FindGlobal(IReadOnlyList<CacheInvalidation> changes) =>
        changes.ToList().FindIndex(change => change.Scope is CacheInvalidationScope.Global);
}
