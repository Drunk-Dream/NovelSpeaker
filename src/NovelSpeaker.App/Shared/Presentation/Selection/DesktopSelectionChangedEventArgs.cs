namespace NovelSpeaker.App.Shared.Presentation.Selection;

public sealed class DesktopSelectionChangedEventArgs<TKey> : EventArgs
    where TKey : notnull
{
    internal DesktopSelectionChangedEventArgs(
        IReadOnlyList<TKey> changedItems,
        bool hasAnchor,
        TKey? anchorItem,
        bool hasPrimary,
        TKey? primaryItem)
    {
        ChangedItems = changedItems;
        HasAnchor = hasAnchor;
        AnchorItem = anchorItem;
        HasPrimary = hasPrimary;
        PrimaryItem = primaryItem;
    }

    public IReadOnlyList<TKey> ChangedItems { get; }

    public bool HasAnchor { get; }

    public TKey? AnchorItem { get; }

    public bool HasPrimary { get; }

    public TKey? PrimaryItem { get; }
}
