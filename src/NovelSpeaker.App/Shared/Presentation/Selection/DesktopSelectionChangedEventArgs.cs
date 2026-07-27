namespace NovelSpeaker.App.Shared.Presentation.Selection;

public sealed class DesktopSelectionChangedEventArgs<TKey> : EventArgs
    where TKey : notnull
{
    internal DesktopSelectionChangedEventArgs(
        IReadOnlyList<TKey> selectedItems,
        bool hasAnchor,
        TKey? anchorItem,
        bool hasPrimary,
        TKey? primaryItem)
    {
        SelectedItems = selectedItems;
        HasAnchor = hasAnchor;
        AnchorItem = anchorItem;
        HasPrimary = hasPrimary;
        PrimaryItem = primaryItem;
    }

    public IReadOnlyList<TKey> SelectedItems { get; }

    public bool HasAnchor { get; }

    public TKey? AnchorItem { get; }

    public bool HasPrimary { get; }

    public TKey? PrimaryItem { get; }
}
