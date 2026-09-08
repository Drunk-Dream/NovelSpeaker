namespace NovelSpeaker.App.Shared.Presentation.Selection;

/// <summary>
/// Owns file-manager-style selection by stable item key. The state does not depend on
/// WPF item containers, so recycling or unrealized virtualized containers cannot lose it.
/// </summary>
public sealed class DesktopSelectionController<TKey>
    where TKey : notnull
{
    private readonly IEqualityComparer<TKey> _comparer;
    private IReadOnlyList<TKey> _items = [];
    private IReadOnlyDictionary<TKey, int> _itemPositions;
    private readonly HashSet<TKey> _selected;
    private readonly HashSet<TKey> _pendingChangedItems;
    private readonly List<TKey> _selectedItems = [];
    private readonly IReadOnlyList<TKey> _selectedItemsView;
    private TKey? _anchorItem;
    private TKey? _primaryItem;

    public DesktopSelectionController(IEqualityComparer<TKey>? comparer = null)
    {
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _itemPositions = new Dictionary<TKey, int>(_comparer);
        _selected = new HashSet<TKey>(_comparer);
        _pendingChangedItems = new HashSet<TKey>(_comparer);
        _selectedItemsView = new SelectedItemsView(_selectedItems);
    }

    public event EventHandler<DesktopSelectionChangedEventArgs<TKey>>? SelectionChanged;

    public IReadOnlyList<TKey> SelectedItems => _selectedItemsView;

    public int Count => _selected.Count;

    public bool HasAnchor { get; private set; }

    public TKey? AnchorItem => _anchorItem;

    public bool HasPrimary { get; private set; }

    public TKey? PrimaryItem => _primaryItem;

    public bool IsSelected(TKey item) => _selected.Contains(item);

    public void SetItems(IEnumerable<TKey> items, bool resetSelection = false)
    {
        ArgumentNullException.ThrowIfNull(items);

        var replacement = items.ToList();
        var replacementSet = new HashSet<TKey>(_comparer);
        foreach (var item in replacement)
        {
            if (!replacementSet.Add(item))
            {
                throw new ArgumentException("Selection item keys must be unique.", nameof(items));
            }
        }

        var positions = replacement
            .Select((item, index) => (item, index))
            .ToDictionary(item => item.item, item => item.index, _comparer);
        SetIndexedItems(replacement, positions, resetSelection);
    }

    public void SetIndexedItems(
        IReadOnlyList<TKey> items,
        IReadOnlyDictionary<TKey, int> positions,
        bool resetSelection = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);

        var itemsAreUnchanged = !resetSelection && _items.SequenceEqual(items, _comparer);
        if (itemsAreUnchanged && !resetSelection)
        {
            return;
        }

        if (resetSelection)
        {
            foreach (var selectedItem in _selected)
            {
                MarkChanged(selectedItem);
            }

            _selected.Clear();
            _selectedItems.Clear();
            ResetMetadata();
        }

        if (itemsAreUnchanged)
        {
            PublishChange();
            return;
        }

        _items = items;
        _itemPositions = positions;

        foreach (var item in _selected
                     .Where(item => !_itemPositions.ContainsKey(item))
                     .ToArray())
        {
            MarkChanged(item);
            _selected.Remove(item);
        }

        _selectedItems.Clear();
        if (_selected.Count > 0)
        {
            foreach (var item in _items)
            {
                if (_selected.Contains(item))
                {
                    _selectedItems.Add(item);
                }
            }
        }

        if (_selected.Count == 0)
        {
            ResetMetadata();
        }
        else
        {
            if (!HasPrimary || !_selected.Contains(_primaryItem!))
            {
                SetPrimary(FirstSelectedItem());
            }

            if (!HasAnchor || !_itemPositions.ContainsKey(_anchorItem!))
            {
                SetAnchor(_primaryItem!);
            }
        }

        PublishChange();
    }

    public void UpdateIndexedItems(
        IReadOnlyList<TKey> items,
        IReadOnlyDictionary<TKey, int> positions)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);

        _items = items;
        _itemPositions = positions;

        foreach (var item in _selected
                     .Where(item => !_itemPositions.ContainsKey(item))
                     .ToArray())
        {
            MarkChanged(item);
            _selected.Remove(item);
        }

        _selectedItems.Clear();
        foreach (var item in _items)
        {
            if (_selected.Contains(item))
            {
                _selectedItems.Add(item);
            }
        }

        if (_selected.Count == 0)
        {
            ResetMetadata();
        }
        else
        {
            if (!HasPrimary || !_selected.Contains(_primaryItem!))
            {
                SetPrimary(FirstSelectedItem());
            }

            if (!HasAnchor || !_itemPositions.ContainsKey(_anchorItem!))
            {
                SetAnchor(_primaryItem!);
            }
        }

        PublishChange();
    }

    public void Click(TKey item, DesktopSelectionModifiers modifiers = DesktopSelectionModifiers.None)
    {
        var itemIndex = FindIndex(item);
        if (itemIndex < 0)
        {
            return;
        }

        var usesControl = modifiers.HasFlag(DesktopSelectionModifiers.Control);
        var usesShift = modifiers.HasFlag(DesktopSelectionModifiers.Shift);
        if (usesShift)
        {
            SelectRange(item, itemIndex, preserveExisting: usesControl);
        }
        else if (usesControl)
        {
            Toggle(item);
        }
        else
        {
            foreach (var selectedItem in _selected)
            {
                if (!_comparer.Equals(selectedItem, item))
                {
                    MarkChanged(selectedItem);
                }
            }

            if (!_selected.Contains(item))
            {
                MarkChanged(item);
            }

            _selected.Clear();
            _selected.Add(item);
            _selectedItems.Clear();
            _selectedItems.Add(item);
            SetAnchor(item);
            SetPrimary(item);
        }

        PublishChange();
    }

    public void SelectAll()
    {
        if (_items.Count == 0)
        {
            Clear();
            return;
        }

        foreach (var item in _items)
        {
            if (!_selected.Contains(item))
            {
                MarkChanged(item);
            }
        }

        _selected.Clear();
        _selected.UnionWith(_items);
        _selectedItems.Clear();
        _selectedItems.AddRange(_items);

        if (!HasPrimary || !_selected.Contains(_primaryItem!))
        {
            SetPrimary(_items[0]);
        }

        if (!HasAnchor || !_selected.Contains(_anchorItem!))
        {
            SetAnchor(_primaryItem!);
        }

        PublishChange();
    }

    public void Clear()
    {
        if (_selected.Count == 0 && !HasAnchor && !HasPrimary)
        {
            return;
        }

        foreach (var selectedItem in _selected)
        {
            MarkChanged(selectedItem);
        }

        _selected.Clear();
        _selectedItems.Clear();
        ResetMetadata();
        PublishChange();
    }

    private void SelectRange(TKey item, int itemIndex, bool preserveExisting)
    {
        var anchorIndex = HasAnchor ? FindIndex(_anchorItem!) : -1;
        if (anchorIndex < 0)
        {
            anchorIndex = HasPrimary ? FindIndex(_primaryItem!) : -1;
        }

        if (anchorIndex < 0)
        {
            anchorIndex = itemIndex;
            SetAnchor(item);
        }

        var previousSelected = _selected.ToHashSet(_comparer);
        if (!preserveExisting)
        {
            _selected.Clear();
            _selectedItems.Clear();
        }

        var start = Math.Min(anchorIndex, itemIndex);
        var end = Math.Max(anchorIndex, itemIndex);
        for (var index = start; index <= end; index++)
        {
            var rangeItem = _items[index];
            AddSelected(rangeItem);
        }

        foreach (var selectedItem in previousSelected)
        {
            if (!_selected.Contains(selectedItem))
            {
                MarkChanged(selectedItem);
            }
        }

        foreach (var selectedItem in _selected)
        {
            if (!previousSelected.Contains(selectedItem))
            {
                MarkChanged(selectedItem);
            }
        }

        SetPrimary(item);
    }

    private void Toggle(TKey item)
    {
        SetAnchor(item);
        if (RemoveSelected(item))
        {
            MarkChanged(item);
            if (_selected.Count == 0)
            {
                HasPrimary = false;
                _primaryItem = default;
            }
            else if (!HasPrimary || _comparer.Equals(_primaryItem!, item))
            {
                SetPrimary(FirstSelectedItem());
            }

            return;
        }

        AddSelected(item);
        MarkChanged(item);
        SetPrimary(item);
    }

    private TKey FirstSelectedItem() => _selectedItems[0];

    private int FindIndex(TKey item) =>
        _itemPositions.TryGetValue(item, out var position) ? position : -1;

    private void SetAnchor(TKey item)
    {
        _anchorItem = item;
        HasAnchor = true;
    }

    private void SetPrimary(TKey item)
    {
        _primaryItem = item;
        HasPrimary = true;
    }

    private void ResetMetadata()
    {
        _anchorItem = default;
        _primaryItem = default;
        HasAnchor = false;
        HasPrimary = false;
    }

    private void PublishChange()
    {
        var changedItems = _pendingChangedItems.ToArray();
        _pendingChangedItems.Clear();
        SelectionChanged?.Invoke(
            this,
            new DesktopSelectionChangedEventArgs<TKey>(
                changedItems,
                HasAnchor,
                _anchorItem,
                HasPrimary,
                _primaryItem));
    }

    private void MarkChanged(TKey item) => _pendingChangedItems.Add(item);

    private bool AddSelected(TKey item)
    {
        if (!_selected.Add(item))
        {
            return false;
        }

        var itemPosition = _itemPositions[item];
        var low = 0;
        var high = _selectedItems.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_itemPositions[_selectedItems[middle]] < itemPosition)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        _selectedItems.Insert(low, item);
        return true;
    }

    private bool RemoveSelected(TKey item)
    {
        if (!_selected.Remove(item))
        {
            return false;
        }

        var itemPosition = _itemPositions[item];
        var low = 0;
        var high = _selectedItems.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var middlePosition = _itemPositions[_selectedItems[middle]];
            if (middlePosition < itemPosition)
            {
                low = middle + 1;
            }
            else if (middlePosition > itemPosition)
            {
                high = middle - 1;
            }
            else
            {
                _selectedItems.RemoveAt(middle);
                break;
            }
        }

        return true;
    }

    private bool IsInRange(TKey item, int anchorIndex, int itemIndex)
    {
        var index = FindIndex(item);
        var start = Math.Min(anchorIndex, itemIndex);
        var end = Math.Max(anchorIndex, itemIndex);
        return index >= start && index <= end;
    }

    private sealed class SelectedItemsView(List<TKey> items) : IReadOnlyList<TKey>
    {
        public int Count => items.Count;

        public TKey this[int index] => items[index];

        public IEnumerator<TKey> GetEnumerator() => items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
