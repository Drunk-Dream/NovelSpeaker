using System.Collections.ObjectModel;

namespace NovelSpeaker.App.Features.Books.Library;

public static class LibraryResponsiveLayout
{
    public const double DefaultMinItemWidth = 300d;
    public const double DefaultMaxItemWidth = 360d;
    public const double DefaultSpacing = 16d;

    public static LibraryResponsiveLayoutResult Create(
        IReadOnlyList<LibraryBookCardProjection> books,
        double availableWidth,
        double minItemWidth = DefaultMinItemWidth,
        double maxItemWidth = DefaultMaxItemWidth,
        double spacing = DefaultSpacing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(books);
        cancellationToken.ThrowIfCancellationRequested();

        if (!double.IsFinite(availableWidth) || availableWidth <= 0d || books.Count == 0)
        {
            return LibraryResponsiveLayoutResult.Empty;
        }

        minItemWidth = CoercePositive(minItemWidth, DefaultMinItemWidth);
        maxItemWidth = Math.Max(minItemWidth, CoercePositive(maxItemWidth, DefaultMaxItemWidth));
        spacing = CoerceNonNegative(spacing, DefaultSpacing);

        var columnsByWidth = Math.Max(
            1,
            (int)Math.Floor((availableWidth + spacing) / (minItemWidth + spacing)));
        var columnCount = Math.Max(1, Math.Min(books.Count, columnsByWidth));
        var rawCardWidth = Math.Max(
            0d,
            (availableWidth - ((columnCount - 1) * spacing)) / columnCount);
        var cardWidth = Math.Min(maxItemWidth, rawCardWidth);
        var rowCount = (int)Math.Ceiling(books.Count / (double)columnCount);
        var rows = new LibraryBookRowProjection[rowCount];
        var bookPositions = new Dictionary<string, LibraryBookRowPosition>(
            books.Count,
            StringComparer.Ordinal);

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowStart = rowIndex * columnCount;
            var rowCountForRow = Math.Min(columnCount, books.Count - rowStart);
            var cards = new LibraryBookRowCardProjection[rowCountForRow];
            for (var columnIndex = 0; columnIndex < rowCountForRow; columnIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var book = books[rowStart + columnIndex];
                cards[columnIndex] = new LibraryBookRowCardProjection(
                    book,
                    columnIndex == rowCountForRow - 1);
                bookPositions[book.BookId] = new LibraryBookRowPosition(rowIndex, columnIndex);
            }

            rows[rowIndex] = new LibraryBookRowProjection(
                rowIndex,
                columnCount,
                cardWidth,
                spacing,
                cards);
        }

        return new LibraryResponsiveLayoutResult(
            columnCount,
            cardWidth,
            rows,
            bookPositions);
    }

    private static double CoercePositive(double value, double fallback) =>
        double.IsFinite(value) && value > 0d ? value : fallback;

    private static double CoerceNonNegative(double value, double fallback) =>
        double.IsFinite(value) && value >= 0d ? value : fallback;
}

public sealed class LibraryResponsiveLayoutResult
{
    public static LibraryResponsiveLayoutResult Empty { get; } = new(
        0,
        0d,
        [],
        new Dictionary<string, LibraryBookRowPosition>(StringComparer.Ordinal));

    public LibraryResponsiveLayoutResult(
        int columnCount,
        double cardWidth,
        IReadOnlyList<LibraryBookRowProjection> rows,
        IReadOnlyDictionary<string, LibraryBookRowPosition> bookPositions)
    {
        ColumnCount = columnCount;
        CardWidth = cardWidth;
        Rows = rows;
        BookPositions = bookPositions;
    }

    public int ColumnCount { get; }

    public double CardWidth { get; }

    public IReadOnlyList<LibraryBookRowProjection> Rows { get; }

    public IReadOnlyDictionary<string, LibraryBookRowPosition> BookPositions { get; }
}

public readonly record struct LibraryBookRowPosition(int RowIndex, int ColumnIndex);

public sealed record LibraryBookRowCardProjection(
    LibraryBookCardProjection Book,
    bool IsLast);

public sealed class LibraryBookRowProjection
{
    public LibraryBookRowProjection(
        int rowIndex,
        int columnCount,
        double cardWidth,
        double spacing,
        IReadOnlyList<LibraryBookRowCardProjection> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        RowIndex = rowIndex;
        ColumnCount = columnCount;
        CardWidth = cardWidth;
        Spacing = spacing;
        Cards = new ObservableCollection<LibraryBookRowCardProjection>(cards);
    }

    public int RowIndex { get; }

    public int ColumnCount { get; }

    public double CardWidth { get; }

    public double Spacing { get; }

    public ObservableCollection<LibraryBookRowCardProjection> Cards { get; }

    public double GroupWidth =>
        (Cards.Count * CardWidth) + (Math.Max(0, Cards.Count - 1) * Spacing);

    public void UpdateBook(int columnIndex, LibraryBookCardProjection book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if ((uint)columnIndex >= (uint)Cards.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        Cards[columnIndex] = new LibraryBookRowCardProjection(
            book,
            columnIndex == Cards.Count - 1);
    }
}
