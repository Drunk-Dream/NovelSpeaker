using System.Text.RegularExpressions;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Application.Books.RuleEditing;

namespace NovelSpeaker.Application.Books.Import;

/// <summary>
/// Matches title lines with ordered rules and creates chapter ranges over normalized text.
/// </summary>
public sealed class ChapterSplitter : IChapterSplitter
{
    public int? FindFirstExplicitTitleOffset(string normalizedText, IReadOnlyList<ChapterRule> rules)
    {
        var orderedRules = CreateRegexes(rules);
        var start = 0;
        while (start <= normalizedText.Length)
        {
            var newline = normalizedText.IndexOf('\n', start);
            var end = newline < 0 ? normalizedText.Length : newline;
            if (IsTitle(normalizedText.AsSpan(start, end - start), orderedRules)) return start;
            if (newline < 0) break;
            start = newline + 1;
        }
        return null;
    }

    public IReadOnlyList<BookImportChapter> Split(
        string normalizedText,
        IReadOnlyList<ChapterRule> rules,
        bool splitOnBlankLines)
    {
        if (string.IsNullOrWhiteSpace(normalizedText)) return [];

        var orderedRules = CreateRegexes(rules);
        var markers = new List<(int TitleOffset, int ContentOffset, string? Title)>();
        var foundTitle = false;
        var hasBody = false;
        var pendingBlank = false;
        var start = 0;
        while (start <= normalizedText.Length)
        {
            var newline = normalizedText.IndexOf('\n', start);
            var end = newline < 0 ? normalizedText.Length : newline;
            var line = normalizedText.AsSpan(start, end - start);
            if (IsTitle(line, orderedRules))
            {
                // An explicit title makes all preceding text a metadata header, including blank sections.
                if (!foundTitle) markers.Clear();
                foundTitle = true;
                markers.Add((start, newline < 0 ? end : end + 1, line.Trim().ToString()));
                hasBody = false;
                pendingBlank = false;
            }
            else if (line.IsWhiteSpace())
            {
                pendingBlank |= hasBody && splitOnBlankLines;
            }
            else
            {
                if (markers.Count == 0 || pendingBlank)
                {
                    markers.Add((start, start, null));
                    hasBody = false;
                }

                hasBody = true;
                pendingBlank = false;
            }
            if (newline < 0) break;
            start = newline + 1;
        }

        if (!foundTitle && !splitOnBlankLines)
            return [new BookImportChapter(0, 0, "全文", 0, normalizedText.Length)];

        var chapters = new List<BookImportChapter>();
        for (var index = 0; index < markers.Count; index++)
        {
            var current = markers[index];
            var nextOffset = index + 1 < markers.Count ? markers[index + 1].TitleOffset : normalizedText.Length;
            var contentLength = nextOffset - current.ContentOffset;
            if (contentLength <= 0 || normalizedText.AsSpan(current.ContentOffset, contentLength).IsWhiteSpace())
            {
                continue;
            }

            chapters.Add(new BookImportChapter(
                chapters.Count,
                current.TitleOffset,
                current.Title ?? $"第 {chapters.Count + 1} 节",
                current.ContentOffset,
                contentLength));
        }

        return chapters.Count == 0
            ? [new BookImportChapter(0, 0, "全文", 0, normalizedText.Length)]
            : chapters;
    }

    private static Regex[] CreateRegexes(IReadOnlyList<ChapterRule> rules) =>
        rules.Where(rule => rule.IsEnabled)
            .OrderBy(rule => rule.SortOrder)
            .Select(rule => ChapterRuleRegexPolicy.Create(rule.Pattern))
            .ToArray();

    private static bool IsTitle(ReadOnlySpan<char> line, Regex[] orderedRules)
    {
        foreach (var regex in orderedRules)
        {
            if (regex.IsMatch(line)) return true;
        }
        return false;
    }
}
