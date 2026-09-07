namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Identifies the smallest cache area that a committed mutation made stale.
/// </summary>
public abstract record CacheInvalidationScope
{
    public sealed record Global : CacheInvalidationScope;

    public sealed record Book : CacheInvalidationScope
    {
        public Book(string bookId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
            BookId = bookId;
        }

        public string BookId { get; }
    }

    public sealed record Chapters : CacheInvalidationScope
    {
        public Chapters(string bookId, IEnumerable<int> chapterIndices)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
            ArgumentNullException.ThrowIfNull(chapterIndices);

            var normalized = chapterIndices
                .Distinct()
                .Order()
                .ToArray();
            if (normalized.Length == 0 || normalized[0] < 0)
            {
                throw new ArgumentException("At least one non-negative chapter index is required.", nameof(chapterIndices));
            }

            BookId = bookId;
            ChapterIndices = Array.AsReadOnly(normalized);
        }

        public string BookId { get; }

        public IReadOnlyList<int> ChapterIndices { get; }
    }
}
