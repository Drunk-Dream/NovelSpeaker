using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Shared.Presentation;

/// <summary>
/// Allows an owner to publish one reset after mutating several existing items without
/// exposing an item-by-item collection notification sequence to the UI.
/// </summary>
internal sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    private readonly SemaphoreSlim _replacementGate = new(1, 1);
    private bool _suppressNotifications;
    private bool _projectionPending;
    private int _replacementVersion;
    private readonly Dictionary<int, T> _pendingReplacements = [];

    public bool IsReplacing => _suppressNotifications;

    public bool IsProjectionPending => _projectionPending;

    public void ReplaceWith<TSource>(
        IEnumerable<TSource> items,
        Func<TSource, T> projector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(projector);

        var materializedItems = items as IReadOnlyList<TSource> ?? items.ToArray();
        var projectedItems = new T[materializedItems.Count];
        for (var index = 0; index < materializedItems.Count; index++)
        {
            projectedItems[index] = projector(materializedItems[index]);
        }

        ReplaceWith(projectedItems);
    }

    public void ReplaceWith(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // A synchronous replacement can be requested while a staged projection is
        // waiting on the UI scheduler. Invalidate the queued callbacks before taking
        // ownership of the collection contents.
        if (_suppressNotifications || _projectionPending)
        {
            Interlocked.Increment(ref _replacementVersion);
            _projectionPending = false;
            _pendingReplacements.Clear();
            _suppressNotifications = false;
        }

        _suppressNotifications = true;
        try
        {
            base.ClearItems();
            for (var index = 0; index < items.Count; index++)
            {
                base.InsertItem(index, items[index]);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        NotifyReset();
    }

    public async Task ReplaceWithInBatchesAsync<TSource>(
        IReadOnlyList<TSource> items,
        Func<TSource, T> projector,
        IUiScheduler uiScheduler,
        CancellationToken cancellationToken,
        int batchSize = 256,
        bool notifyEachBatch = true,
        bool preservePreviousItemsOnCancel = false,
        Func<bool>? isCurrent = null,
        Action? beforeComplete = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(uiScheduler);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var requestedReplacementVersion = Volatile.Read(ref _replacementVersion);
        await _replacementGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (requestedReplacementVersion != Volatile.Read(ref _replacementVersion))
            {
                return;
            }

            await ReplaceWithInBatchesCoreAsync(
                items,
                projector,
                uiScheduler,
                cancellationToken,
                batchSize,
                notifyEachBatch,
                preservePreviousItemsOnCancel,
                isCurrent,
                beforeComplete).ConfigureAwait(true);
        }
        finally
        {
            _replacementGate.Release();
        }
    }

    private async Task ReplaceWithInBatchesCoreAsync<TSource>(
        IReadOnlyList<TSource> items,
        Func<TSource, T> projector,
        IUiScheduler uiScheduler,
        CancellationToken cancellationToken,
        int batchSize,
        bool notifyEachBatch,
        bool preservePreviousItemsOnCancel,
        Func<bool>? isCurrent,
        Action? beforeComplete)
    {

        // Small lists do not justify crossing an asynchronous boundary. This also keeps
        // ordinary page activation deterministic while large catalogs use staged work.
        if (items.Count < batchSize * 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectedSmallItems = new T[items.Count];
            for (var index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                projectedSmallItems[index] = projector(items[index]);
            }

            await uiScheduler.InvokeAsync(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfProjectionIsStale(isCurrent, cancellationToken);
                    ReplaceWith(projectedSmallItems);
                    beforeComplete?.Invoke();
                },
                cancellationToken).ConfigureAwait(true);
            return;
        }

        _projectionPending = true;
        _pendingReplacements.Clear();
        var replacementVersion = Volatile.Read(ref _replacementVersion);
        var previousItems = preservePreviousItemsOnCancel ? this.ToArray() : null;
        try
        {
            var projectedItems = await Task.Run(
                () =>
                {
                    var result = new T[items.Count];
                    for (var index = 0; index < items.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ThrowIfProjectionIsStale(isCurrent, cancellationToken);
                        result[index] = projector(items[index]);
                    }

                    return result;
                },
                cancellationToken).ConfigureAwait(true);

            try
            {
                await uiScheduler.InvokeLaterAsync(
                    () => BeginReplace(replacementVersion, isCurrent, cancellationToken),
                    cancellationToken).ConfigureAwait(true);
                for (var offset = 0; offset < projectedItems.Length; offset += batchSize)
                {
                    var batchOffset = offset;
                    var batchCount = Math.Min(batchSize, projectedItems.Length - offset);
                    await uiScheduler.InvokeLaterAsync(
                        () => AppendBatch(
                            projectedItems,
                            batchOffset,
                            batchCount,
                            replacementVersion,
                            notifyEachBatch,
                            isCurrent,
                            cancellationToken),
                        cancellationToken).ConfigureAwait(true);
                }

                await uiScheduler.InvokeLaterAsync(
                    () => CompleteReplace(
                        replacementVersion,
                        isCurrent,
                        cancellationToken,
                        beforeComplete),
                    cancellationToken).ConfigureAwait(true);
            }
            catch
            {
                try
                {
                    await uiScheduler.InvokeLaterAsync(
                        () => AbortReplace(replacementVersion, previousItems)).ConfigureAwait(true);
                }
                catch
                {
                    // Preserve the original projection/cancellation failure.
                }

                throw;
            }
        }
        finally
        {
            _projectionPending = false;
            _pendingReplacements.Clear();
        }
    }

    public void NotifyReset()
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void NotifyResetWhileReplacing()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void BeginReplace(
        int replacementVersion,
        Func<bool>? isCurrent,
        CancellationToken cancellationToken)
    {
        ThrowIfProjectionIsStale(isCurrent, cancellationToken);
        if (replacementVersion != Volatile.Read(ref _replacementVersion))
        {
            return;
        }

        if (_suppressNotifications)
        {
            throw new InvalidOperationException("A collection replacement is already active.");
        }

        _suppressNotifications = true;
        base.ClearItems();
    }

    private void AppendBatch(
        IReadOnlyList<T> items,
        int offset,
        int count,
        int replacementVersion,
        bool notifyEachBatch,
        Func<bool>? isCurrent,
        CancellationToken cancellationToken)
    {
        ThrowIfProjectionIsStale(isCurrent, cancellationToken);
        if (replacementVersion != Volatile.Read(ref _replacementVersion))
        {
            return;
        }

        if (!_suppressNotifications)
        {
            throw new InvalidOperationException("A collection replacement is not active.");
        }

        for (var index = 0; index < count; index++)
        {
            var actualIndex = offset + index;
            base.InsertItem(
                actualIndex,
                _pendingReplacements.Remove(actualIndex, out var replacement)
                    ? replacement
                    : items[actualIndex]);
        }

        // Owners with an already materialized projection can defer notification until
        // the final reset, avoiding repeated whole-grid layout work for non-virtualized
        // surfaces while still yielding between bounded UI mutations.
        if (notifyEachBatch)
        {
            NotifyResetWhileReplacing();
        }
    }

    private void CompleteReplace(
        int replacementVersion,
        Func<bool>? isCurrent,
        CancellationToken cancellationToken,
        Action? beforeComplete)
    {
        ThrowIfProjectionIsStale(isCurrent, cancellationToken);
        if (replacementVersion != Volatile.Read(ref _replacementVersion))
        {
            return;
        }

        beforeComplete?.Invoke();
        ThrowIfProjectionIsStale(isCurrent, cancellationToken);

        if (!_suppressNotifications)
        {
            throw new InvalidOperationException("A collection replacement is not active.");
        }

        _suppressNotifications = false;
        _projectionPending = false;
        _pendingReplacements.Clear();
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        NotifyReset();
    }

    private static void ThrowIfProjectionIsStale(
        Func<bool>? isCurrent,
        CancellationToken cancellationToken)
    {
        if (isCurrent is not null && !isCurrent())
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private void AbortReplace(int replacementVersion, IReadOnlyList<T>? previousItems)
    {
        if (replacementVersion != Volatile.Read(ref _replacementVersion) ||
            !_suppressNotifications)
        {
            return;
        }

        var pendingReplacements = previousItems is not null
            ? _pendingReplacements.ToArray()
            : null;
        base.ClearItems();
        if (previousItems is not null)
        {
            for (var index = 0; index < previousItems.Count; index++)
            {
                base.InsertItem(index, previousItems[index]);
            }

            if (pendingReplacements is not null)
            {
                foreach (var (index, replacement) in pendingReplacements)
                {
                    if ((uint)index < (uint)previousItems.Count)
                    {
                        base.SetItem(index, replacement);
                    }
                }
            }
        }
        _pendingReplacements.Clear();
        _suppressNotifications = false;
        _projectionPending = false;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        NotifyReset();
    }

    public void ReplaceAt(int index, T item, bool notify = true)
    {
        if (_suppressNotifications)
        {
            if ((uint)index < (uint)Count)
            {
                base.SetItem(index, item);
            }
            else
            {
                _pendingReplacements[index] = item;
            }

            return;
        }

        if (_projectionPending)
        {
            _pendingReplacements[index] = item;
            return;
        }

        if (notify)
        {
            SetItem(index, item);
            return;
        }

        _suppressNotifications = true;
        try
        {
            base.SetItem(index, item);
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    public void ReplaceAtMany(
        IReadOnlyList<(int Index, T Item)> replacements,
        bool notify = true)
    {
        ArgumentNullException.ThrowIfNull(replacements);

        if (_suppressNotifications)
        {
            foreach (var (index, item) in replacements)
            {
                if ((uint)index < (uint)Count)
                {
                    base.SetItem(index, item);
                }
                else
                {
                    _pendingReplacements[index] = item;
                }
            }

            return;
        }

        if (_projectionPending)
        {
            foreach (var (index, item) in replacements)
            {
                _pendingReplacements[index] = item;
            }

            return;
        }

        _suppressNotifications = true;
        try
        {
            foreach (var (index, item) in replacements)
            {
                base.SetItem(index, item);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        if (notify && replacements.Count > 0)
        {
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            NotifyReset();
        }
    }

    public void QueueReplacement(int index, T item) => _pendingReplacements[index] = item;

    protected override void ClearItems()
    {
        if (!_suppressNotifications && !_projectionPending)
        {
            base.ClearItems();
            return;
        }

        Interlocked.Increment(ref _replacementVersion);
        _projectionPending = false;
        _pendingReplacements.Clear();
        _suppressNotifications = true;
        try
        {
            base.ClearItems();
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        NotifyReset();
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotifications)
        {
            base.OnPropertyChanged(e);
        }
    }
}
