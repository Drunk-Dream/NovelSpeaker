namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Book-level ordinal and raw UTF-16 character position, independent of binding and
/// technical chapter identity. Runtime segment/audio positions are resolved separately.
/// </summary>
public sealed record ReadingState(int ChapterIndex, int CharacterOffset)
{
    /// <summary>
    /// Clamps to a complete replacement catalog's chapter lengths without matching
    /// chapters. Empty catalogs return an unlocated state; empty chapters use offset zero.
    /// </summary>
    public ReadingState? Clamp(IReadOnlyList<int> chapterLengths)
    {
        ArgumentNullException.ThrowIfNull(chapterLengths);
        if (chapterLengths.Count == 0)
            return null;

        var chapterIndex = Math.Clamp(ChapterIndex, 0, chapterLengths.Count - 1);
        var length = chapterLengths[chapterIndex];
        ArgumentOutOfRangeException.ThrowIfNegative(length, nameof(chapterLengths));
        return new ReadingState(chapterIndex, Math.Clamp(CharacterOffset, 0, Math.Max(0, length - 1)));
    }
}
