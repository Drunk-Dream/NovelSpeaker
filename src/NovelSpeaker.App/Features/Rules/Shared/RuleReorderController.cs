namespace NovelSpeaker.App.Features.Rules.Shared;

/// <summary>
/// Computes rule order changes without mutating a page collection or persistence state.
/// </summary>
internal static class RuleReorderController
{
    /// <summary>Maps a visible slot into the complete order without moving hidden items independently.</summary>
    public static bool TryMoveToSlot<TId>(
        IReadOnlyList<TId> currentOrder,
        IReadOnlyList<TId> visibleOrder,
        TId sourceId,
        int slotIndex,
        out IReadOnlyList<TId> reordered,
        IEqualityComparer<TId>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(currentOrder);
        ArgumentNullException.ThrowIfNull(visibleOrder);
        comparer ??= EqualityComparer<TId>.Default;
        reordered = [];
        if (slotIndex < 0 || slotIndex > visibleOrder.Count || visibleOrder.Count == 0 ||
            IndexOf(visibleOrder, sourceId, comparer) < 0 ||
            IndexOf(currentOrder, sourceId, comparer) < 0)
        {
            return false;
        }

        // Every interior slot has exactly one anchor: the following visible item.
        var atEnd = slotIndex == visibleOrder.Count;
        var anchor = visibleOrder[atEnd ? slotIndex - 1 : slotIndex];
        if (comparer.Equals(sourceId, anchor))
        {
            return false;
        }

        var result = currentOrder.ToList();
        result.RemoveAt(IndexOf(result, sourceId, comparer));
        var anchorIndex = IndexOf(result, anchor, comparer);
        if (anchorIndex < 0)
        {
            return false;
        }

        result.Insert(anchorIndex + (atEnd ? 1 : 0), sourceId);
        if (!HasOrderChanged(currentOrder, result, comparer))
        {
            return false;
        }

        reordered = result;
        return true;
    }

    public static bool TryMoveByOffset<TId>(
        IReadOnlyList<TId> currentOrder,
        TId itemId,
        int offset,
        out IReadOnlyList<TId> reordered,
        IEqualityComparer<TId>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(currentOrder);
        comparer ??= EqualityComparer<TId>.Default;

        var sourceIndex = IndexOf(currentOrder, itemId, comparer);
        var targetIndex = sourceIndex + offset;
        if (offset == 0 || sourceIndex < 0 || targetIndex < 0 || targetIndex >= currentOrder.Count)
        {
            reordered = [];
            return false;
        }

        var result = currentOrder.ToList();
        (result[sourceIndex], result[targetIndex]) = (result[targetIndex], result[sourceIndex]);
        if (!HasOrderChanged(currentOrder, result, comparer))
        {
            reordered = [];
            return false;
        }

        reordered = result;
        return true;
    }

    private static bool HasOrderChanged<TId>(
        IReadOnlyList<TId> original,
        IReadOnlyList<TId> candidate,
        IEqualityComparer<TId> comparer)
    {
        if (original.Count != candidate.Count)
        {
            return true;
        }

        for (var index = 0; index < original.Count; index++)
        {
            if (!comparer.Equals(original[index], candidate[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOf<TId>(IReadOnlyList<TId> values, TId value, IEqualityComparer<TId> comparer)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (comparer.Equals(values[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
