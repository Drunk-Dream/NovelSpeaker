using System.Text.RegularExpressions;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books.Import;

/// <summary>
/// Matches title lines with ordered rules and creates chapter ranges over normalized text.
/// </summary>
public sealed class ChapterSplitter : IChapterSplitter
{
    private static readonly Regex MultiWhitespace = new(@"\s+", RegexOptions.CultureInvariant);

    public IReadOnlyList<BookImportChapter> Split(string normalizedText, IReadOnlyList<ChapterRule> rules)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return [];
        }

        var markers = new List<(int TitleOffset, int ContentOffset, string Title)>();
        var orderedRules = rules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.SortOrder).ToArray();
        var lineStart = 0;

        foreach (var line in normalizedText.Split('\n'))
        {
            var matchedRule = orderedRules.FirstOrDefault(
                rule => Regex.IsMatch(line, rule.Pattern, RegexOptions.CultureInvariant));
            if (matchedRule is not null)
            {
                markers.Add((lineStart, lineStart + line.Length + 1, CleanTitle(line)));
            }

            lineStart += line.Length + 1;
        }

        if (markers.Count == 0)
        {
            return [new BookImportChapter(0, 0, "全文", 0, normalizedText.Length)];
        }

        var chapters = new List<BookImportChapter>();
        for (var index = 0; index < markers.Count; index++)
        {
            var current = markers[index];
            var nextTitleOffset = index + 1 < markers.Count ? markers[index + 1].TitleOffset : normalizedText.Length;
            var contentLength = nextTitleOffset - current.ContentOffset;
            if (contentLength <= 0)
            {
                continue;
            }

            var content = normalizedText.Substring(current.ContentOffset, contentLength);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            chapters.Add(new BookImportChapter(
                chapters.Count,
                current.TitleOffset,
                current.Title,
                current.ContentOffset,
                contentLength));
        }

        return chapters.Count == 0
            ? [new BookImportChapter(0, 0, "全文", 0, normalizedText.Length)]
            : chapters;
    }

    private static string CleanTitle(string title) => MultiWhitespace.Replace(title.Trim(), " ");
}
