using System.Collections.ObjectModel;

namespace NovelSpeaker.App.ViewModels;

internal static class ViewModelCollectionExtensions
{
    public static void ReplaceWith<TSource, TTarget>(
        this ObservableCollection<TTarget> collection,
        IEnumerable<TSource> items,
        Func<TSource, TTarget> projector)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(projector(item));
        }
    }

    public static T? SelectByKeyOrFallback<T>(
        this IEnumerable<T> items,
        object? selectedKey,
        Func<T, object?> keySelector,
        T? currentSelection = default,
        Func<T, bool>? preferredPredicate = null)
        where T : class
    {
        var materializedItems = items as IReadOnlyList<T> ?? items.ToArray();

        if (selectedKey is not null)
        {
            var bySelectedKey = materializedItems.FirstOrDefault(item => Equals(keySelector(item), selectedKey));
            if (bySelectedKey is not null)
            {
                return bySelectedKey;
            }
        }

        if (currentSelection is not null)
        {
            var currentKey = keySelector(currentSelection);
            if (currentKey is not null)
            {
                var byCurrentKey = materializedItems.FirstOrDefault(item => Equals(keySelector(item), currentKey));
                if (byCurrentKey is not null)
                {
                    return byCurrentKey;
                }
            }
        }

        if (preferredPredicate is not null)
        {
            var preferred = materializedItems.FirstOrDefault(preferredPredicate);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return materializedItems.FirstOrDefault();
    }
}
