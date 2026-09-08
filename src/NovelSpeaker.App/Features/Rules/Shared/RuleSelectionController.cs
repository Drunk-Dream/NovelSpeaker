namespace NovelSpeaker.App.Features.Rules.Shared;

/// <summary>
/// Owns the selected rule key without owning the page's observable item projection.
/// </summary>
internal sealed class RuleSelectionController<TId>
{
    private TId _selectedId = default!;

    public bool HasSelection { get; private set; }

    public TId SelectedId => HasSelection ? _selectedId : default!;

    public void Select(TId id)
    {
        _selectedId = id;
        HasSelection = true;
    }

    public void Clear()
    {
        _selectedId = default!;
        HasSelection = false;
    }

    public bool IsSelected(TId id) =>
        HasSelection &&
        EqualityComparer<TId>.Default.Equals(_selectedId, id);

    public bool TryGetSelected(IReadOnlySet<TId> availableIds, out TId selectedId)
    {
        ArgumentNullException.ThrowIfNull(availableIds);
        if (HasSelection && availableIds.Contains(_selectedId))
        {
            selectedId = _selectedId;
            return true;
        }

        selectedId = default!;
        return false;
    }
}
