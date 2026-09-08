using NovelSpeaker.App.Shared.Presentation.Rules;

namespace NovelSpeaker.App.Features.Rules.Shared;

/// <summary>
/// Computes rule order changes without mutating a page collection or persistence state.
/// </summary>
internal static class RuleReorderController
{
    public static bool TryMove<TId>(
        IReadOnlyList<TId> currentOrder,
        TId sourceId,
        TId targetId,
        RuleDropPlacement placement,
        out IReadOnlyList<TId> reordered,
        IEqualityComparer<TId>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(currentOrder);
        comparer ??= EqualityComparer<TId>.Default;

        if (placement is not (RuleDropPlacement.Before or RuleDropPlacement.After) ||
            comparer.Equals(sourceId, targetId))
        {
            reordered = [];
            return false;
        }

        var sourceIndex = IndexOf(currentOrder, sourceId, comparer);
        var targetIndex = IndexOf(currentOrder, targetId, comparer);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            reordered = [];
            return false;
        }

        var result = currentOrder.ToList();
        result.RemoveAt(sourceIndex);
        targetIndex = IndexOf(result, targetId, comparer);
        var insertionIndex = placement == RuleDropPlacement.After ? targetIndex + 1 : targetIndex;
        result.Insert(insertionIndex, sourceId);
        if (!HasOrderChanged(currentOrder, result, comparer))
        {
            reordered = [];
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
