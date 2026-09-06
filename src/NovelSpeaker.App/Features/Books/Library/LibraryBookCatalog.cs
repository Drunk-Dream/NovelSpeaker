using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Books.Shared;

namespace NovelSpeaker.App.Features.Books.Library;

/// <summary>
/// Holds the immutable library read model and performs filtering/sorting without
/// constructing WPF item state.
/// </summary>
internal sealed class LibraryBookCatalog
{
    private readonly IReadOnlyList<LibraryBookCatalogItem> _items;
    private readonly IReadOnlyDictionary<string, int> _positionsByBookId;

    public LibraryBookCatalog(
        IReadOnlyList<BookSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var items = new LibraryBookCatalogItem[summaries.Count];
        var positionsByBookId = new Dictionary<string, int>(summaries.Count, StringComparer.Ordinal);
        for (var position = 0; position < summaries.Count; position++)
        {
            var summary = summaries[position];
            if (string.IsNullOrWhiteSpace(summary.Id))
            {
                throw new ArgumentException("Library book ids must not be empty.", nameof(summaries));
            }

            if (!positionsByBookId.TryAdd(summary.Id, position))
            {
                throw new ArgumentException("Library book ids must be unique.", nameof(summaries));
            }

            items[position] = new LibraryBookCatalogItem(
                summary,
                string.IsNullOrWhiteSpace(summary.Author) ? "未知作者" : summary.Author.Trim(),
                NormalizeTitleKey(summary.Title));
        }

        _items = Array.AsReadOnly(items);
        _positionsByBookId = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(positionsByBookId);
    }

    public IReadOnlyList<LibraryBookCatalogItem> Items => _items;

    public int Count => _items.Count;

    public bool TryGet(string bookId, out LibraryBookCatalogItem item)
    {
        if (_positionsByBookId.TryGetValue(bookId, out var position))
        {
            item = _items[position];
            return true;
        }

        item = null!;
        return false;
    }

    public IReadOnlyList<LibraryBookCatalogItem> Query(
        string normalizedSearchTerm,
        LibrarySortMode sortMode,
        IReadOnlyDictionary<string, EffectiveReadingProgress>? decorations = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedSearchTerm);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new List<LibraryBookCatalogItem>(_items.Count);
        foreach (var item in _items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.MatchesSearch(normalizedSearchTerm))
            {
                result.Add(item);
            }
        }

        result.Sort((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var comparison = sortMode == LibrarySortMode.Title
                ? StringComparer.Ordinal.Compare(left.SortTitleKey, right.SortTitleKey)
                : CompareRecentReading(left, right, decorations);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.Ordinal.Compare(left.BookId, right.BookId);
        });

        cancellationToken.ThrowIfCancellationRequested();
        return result.ToArray();
    }

    private static int CompareRecentReading(
        LibraryBookCatalogItem left,
        LibraryBookCatalogItem right,
        IReadOnlyDictionary<string, EffectiveReadingProgress>? decorations)
    {
        var comparison = HasReadingProgress(right, decorations).CompareTo(
            HasReadingProgress(left, decorations));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Nullable.Compare(right.Summary.LastPlayedAt, left.Summary.LastPlayedAt);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.SortTitleKey, right.SortTitleKey);
    }

    private static bool HasReadingProgress(
        LibraryBookCatalogItem item,
        IReadOnlyDictionary<string, EffectiveReadingProgress>? decorations)
    {
        return decorations?.TryGetValue(item.BookId, out var decoration) == true
            ? decoration.HasReadingProgress
            : item.Summary.HasReadingProgress;
    }

    public static string NormalizeSearchText(string? value)
    {
        return string.Join(
            ' ',
            (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
    }

    private static string NormalizeTitleKey(string? value)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized)
            ? "未命名书籍"
            : normalized.ToUpperInvariant();
    }
}

internal sealed record LibraryBookCatalogItem(
    BookSummary Summary,
    string DisplayAuthor,
    string SortTitleKey)
{
    public string BookId => Summary.Id;

    private string NormalizedSearchText { get; } =
        $"{LibraryBookCatalog.NormalizeSearchText(Summary.Title)}|{LibraryBookCatalog.NormalizeSearchText(DisplayAuthor)}";

    public bool MatchesSearch(string normalizedSearchTerm) =>
        string.IsNullOrEmpty(normalizedSearchTerm) ||
        NormalizedSearchText.Contains(normalizedSearchTerm, StringComparison.Ordinal);
}
