using System.Collections.ObjectModel;

namespace NovelSpeaker.App.Shared.Presentation;

/// <summary>
/// Holds an immutable catalog and its stable key-to-position index.
/// </summary>
internal sealed class IndexedCatalog<TItem>
{
    private readonly IReadOnlyList<TItem> _items;
    private readonly IReadOnlyDictionary<int, int> _positionsByKey;
    private readonly IReadOnlyList<int> _keys;
    private readonly Func<TItem, int> _keySelector;

    public IndexedCatalog(IReadOnlyList<TItem> items, Func<TItem, int> keySelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);
        _keySelector = keySelector;

        var snapshot = items.ToArray();
        var positionsByKey = new Dictionary<int, int>(snapshot.Length);
        for (var position = 0; position < snapshot.Length; position++)
        {
            if (!positionsByKey.TryAdd(keySelector(snapshot[position]), position))
            {
                throw new ArgumentException("Catalog keys must be unique.", nameof(items));
            }
        }

        _items = Array.AsReadOnly(snapshot);
        _positionsByKey = new ReadOnlyDictionary<int, int>(positionsByKey);
        _keys = Array.AsReadOnly(snapshot.Select(keySelector).ToArray());
    }

    public IReadOnlyList<TItem> Items => _items;

    public int Count => _items.Count;

    public IReadOnlyList<int> Keys => _keys;

    public IReadOnlyDictionary<int, int> Positions => _positionsByKey;

    public TItem this[int position] => _items[position];

    public IReadOnlyList<TItem> Slice(int start, int count)
    {
        if (start < 0 || start > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var length = Math.Min(count, Count - start);
        var slice = new TItem[length];
        for (var index = 0; index < length; index++)
        {
            slice[index] = _items[start + index];
        }

        return slice;
    }

    public IndexedCatalog<TItem> ReplaceAt(int position, TItem item)
    {
        if ((uint)position >= (uint)_items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var replacement = _items.ToArray();
        replacement[position] = item;
        return new IndexedCatalog<TItem>(replacement, _keySelector);
    }

    public bool TryGet(int key, out TItem item)
    {
        if (_positionsByKey.TryGetValue(key, out var position))
        {
            item = _items[position];
            return true;
        }

        item = default!;
        return false;
    }

    public bool TryGetPosition(int key, out int position) =>
        _positionsByKey.TryGetValue(key, out position);
}

/// <summary>
/// Stores only changed decorations for an immutable catalog.
/// </summary>
internal sealed class SparseCatalogDecoration<TDecoration>
{
    private readonly Dictionary<int, TDecoration> _values = [];

    public int Count => _values.Count;

    public bool TryGet(int key, out TDecoration value) => _values.TryGetValue(key, out value!);

    public void Set(int key, TDecoration value) => _values[key] = value;

    public bool Remove(int key) => _values.Remove(key);

    public void Clear() => _values.Clear();

    public IReadOnlyDictionary<int, TDecoration> Snapshot() =>
        new Dictionary<int, TDecoration>(_values);
}
