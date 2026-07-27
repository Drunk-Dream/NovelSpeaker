using System.Collections.ObjectModel;

namespace NovelSpeaker.App.Shared.Presentation.Selection;

/// <summary>
/// Owns file-manager-style selection by stable item key. The state does not depend on
/// WPF item containers, so recycling or unrealized virtualized containers cannot lose it.
/// </summary>
public sealed class DesktopSelectionController<TKey>
    where TKey : notnull
{
    private readonly IEqualityComparer<TKey> _comparer;
    private readonly List<TKey> _items = [];
    private readonly HashSet<TKey> _selected;
    private IReadOnlyList<TKey> _selectedItems = Array.Empty<TKey>();
    private TKey? _anchorItem;
    private TKey? _primaryItem;

    public DesktopSelectionController(IEqualityComparer<TKey>? comparer = null)
    {
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _selected = new HashSet<TKey>(_comparer);
    }

    public event EventHandler<DesktopSelectionChangedEventArgs<TKey>>? SelectionChanged;

    public IReadOnlyList<TKey> SelectedItems => _selectedItems;

    public int Count => _selected.Count;

    public bool HasAnchor { get; private set; }

    public TKey? AnchorItem => _anchorItem;

    public bool HasPrimary { get; private set; }

    public TKey? PrimaryItem => _primaryItem;

    public bool IsSelected(TKey item) => _selected.Contains(item);

    public void SetItems(IEnumerable<TKey> items)
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

        if (_items.SequenceEqual(replacement, _comparer))
        {
            return;
        }

        _items.Clear();
        _items.AddRange(replacement);
        _selected.RemoveWhere(item => !replacementSet.Contains(item));

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

            if (!HasAnchor || !replacementSet.Contains(_anchorItem!))
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
            _selected.Clear();
            _selected.Add(item);
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

        _selected.Clear();
        _selected.UnionWith(_items);

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

        _selected.Clear();
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

        if (!preserveExisting)
        {
            _selected.Clear();
        }

        var start = Math.Min(anchorIndex, itemIndex);
        var end = Math.Max(anchorIndex, itemIndex);
        for (var index = start; index <= end; index++)
        {
            _selected.Add(_items[index]);
        }

        SetPrimary(item);
    }

    private void Toggle(TKey item)
    {
        SetAnchor(item);
        if (_selected.Remove(item))
        {
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

        _selected.Add(item);
        SetPrimary(item);
    }

    private TKey FirstSelectedItem() => _items.First(_selected.Contains);

    private int FindIndex(TKey item) => _items.FindIndex(candidate => _comparer.Equals(candidate, item));

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
        _selectedItems = new ReadOnlyCollection<TKey>(_items.Where(_selected.Contains).ToArray());
        SelectionChanged?.Invoke(
            this,
            new DesktopSelectionChangedEventArgs<TKey>(
                _selectedItems,
                HasAnchor,
                _anchorItem,
                HasPrimary,
                _primaryItem));
    }
}
