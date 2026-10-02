namespace NovelSpeaker.App.Shared.Presentation.Selection;

/// <summary>
/// Owns one page's management mode and stable-key selection. The feature supplies only
/// visible, manageable keys and retains ownership of normal actions and batch commands.
/// Call Enter only after any feature-owned dirty-draft guard has succeeded.
/// </summary>
public sealed class ManagementSelectionController<TKey>
    where TKey : notnull
{
    private readonly DesktopSelectionController<TKey> _selection;

    public ManagementSelectionController(IEqualityComparer<TKey>? comparer = null)
    {
        _selection = new DesktopSelectionController<TKey>(comparer);
        _selection.SelectionChanged += (_, args) => PublishStateChange(args.ChangedItems);
    }

    public event EventHandler? StateChanged;

    public bool IsManagementMode { get; private set; }

    public int SelectedCount => _selection.Count;

    public IReadOnlyList<TKey> SelectedItems => _selection.SelectedItems;

    /// <summary>Keys whose selection decoration changed in the latest notification.</summary>
    public IReadOnlyList<TKey> ChangedItems { get; private set; } = [];

    public bool IsSelected(TKey item) => _selection.IsSelected(item);

    /// <summary>
    /// Reconciles selection with the current visible, manageable set. Sorting or rebuilding
    /// a projection preserves live keys; hidden or removed keys cannot reappear selected.
    /// Reset before replacing a scope whose stable identity cannot be confirmed.
    /// </summary>
    public void SetItems(IEnumerable<TKey> items) => _selection.SetItems(items);

    /// <summary>
    /// Accepts an already indexed visible, manageable snapshot. Items and positions must
    /// agree, use unique stable keys and the controller's comparer, and remain unchanged
    /// until the next snapshot is supplied.
    /// </summary>
    public void SetIndexedItems(IReadOnlyList<TKey> items, IReadOnlyDictionary<TKey, int> positions) =>
        _selection.SetIndexedItems(items, positions);

    public void Enter()
    {
        if (IsManagementMode)
        {
            return;
        }

        IsManagementMode = true;
        PublishStateChange([]);
    }

    /// <summary>Exits management mode and clears selection while retaining the item set.</summary>
    public bool Exit()
    {
        if (!IsManagementMode)
        {
            return false;
        }

        IsManagementMode = false;
        var hadSelectionState = _selection.Count > 0 || _selection.HasAnchor || _selection.HasPrimary;
        _selection.Clear();
        if (!hadSelectionState)
        {
            PublishStateChange([]);
        }

        return true;
    }

    /// <summary>Use on page leave or scope replacement to drop the entire page-owned state.</summary>
    public void Reset()
    {
        IsManagementMode = false;
        _selection.SetItems([], resetSelection: true);
    }

    /// <summary>
    /// Returns false only for a normal-mode plain click, allowing the feature's normal
    /// action. Management clicks (including repeated clicks) and modifier gestures are
    /// consumed. The feature must apply its entry guard before a normal-mode Ctrl/Shift click.
    /// </summary>
    public bool HandleClick(TKey item, DesktopSelectionModifiers modifiers = DesktopSelectionModifiers.None)
    {
        if (!IsManagementMode && modifiers == DesktopSelectionModifiers.None)
        {
            return false;
        }

        if (!_selection.ContainsItem(item))
        {
            return true;
        }

        Enter();
        _selection.Click(item, modifiers == DesktopSelectionModifiers.None
            ? DesktopSelectionModifiers.Control
            : modifiers);
        return true;
    }

    /// <summary>
    /// Returns false in normal mode so the feature retains its existing context-menu
    /// behavior. In management mode an unselected live item replaces the selection.
    /// </summary>
    public bool HandleRightClick(TKey item)
    {
        if (!IsManagementMode)
        {
            return false;
        }

        if (_selection.ContainsItem(item) && !_selection.IsSelected(item))
        {
            _selection.Click(item);
        }

        return true;
    }

    public void SelectAll()
    {
        if (IsManagementMode)
        {
            _selection.SelectAll();
        }
    }

    private void PublishStateChange(IReadOnlyList<TKey> changedItems)
    {
        ChangedItems = changedItems;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
