using System.Text.RegularExpressions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books.Parsing;

/// <summary>
/// Matches title lines with ordered rules and builds persisted chapter ranges.
/// </summary>
public sealed class ChapterSplitter : IChapterSplitter
{
    public IReadOnlyList<BookImportChapter> Split(string normalizedText, IReadOnlyList<ChapterRule> rules)
    {
        if (string.IsNullOrWhiteSpace(normalizedText) || rules.Count == 0)
        {
            return [];
        }

        var markers = new List<(int TitleOffset, int ContentOffset, string Title)>();
        var orderedRules = rules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.SortOrder).ToArray();
        var lineStart = 0;

        foreach (var line in normalizedText.Split('\n'))
        {
            var matchedRule = orderedRules.FirstOrDefault(rule => Regex.IsMatch(line, rule.Pattern, RegexOptions.CultureInvariant));
            if (matchedRule is not null)
            {
                markers.Add((lineStart, lineStart + line.Length + 1, line.Trim()));
            }

            lineStart += line.Length + 1;
        }

        if (markers.Count == 0)
        {
            return [];
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
                current.Title,
                content,
                current.ContentOffset,
                content.Length));
        }

        return chapters;
    }
}
