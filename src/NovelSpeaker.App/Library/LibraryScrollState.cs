namespace NovelSpeaker.App.Library;

public sealed class LibraryScrollState
{
    public string? AnchorBookId { get; private set; }

    public double RelativeOffset { get; private set; }

    public void Capture(IReadOnlyList<LibraryVisibleBookPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var anchor = positions
            .Where(static position => position.Bottom > 0)
            .OrderBy(static position => position.Top)
            .FirstOrDefault();

        if (anchor is null)
        {
            return;
        }

        AnchorBookId = anchor.BookId;
        RelativeOffset = anchor.Top;
    }

    public bool TryGetRestoreOffset(
        IReadOnlyList<LibraryVisibleBookPosition> positions,
        out double verticalOffset)
    {
        ArgumentNullException.ThrowIfNull(positions);

        verticalOffset = 0;
        if (string.IsNullOrWhiteSpace(AnchorBookId))
        {
            return false;
        }

        var anchor = positions.FirstOrDefault(position =>
            string.Equals(position.BookId, AnchorBookId, StringComparison.Ordinal));

        if (anchor is null)
        {
            return false;
        }

        verticalOffset = Math.Max(0, anchor.Top - RelativeOffset);
        return true;
    }

    public void Clear()
    {
        AnchorBookId = null;
        RelativeOffset = 0;
    }
}
