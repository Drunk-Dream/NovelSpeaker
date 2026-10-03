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
        return FindLines(normalizedText, rules).FirstOrDefault(line => line.IsTitle)?.Start;
    }

    public IReadOnlyList<BookImportChapter> Split(
        string normalizedText,
        IReadOnlyList<ChapterRule> rules,
        bool splitOnBlankLines)
    {
        if (string.IsNullOrWhiteSpace(normalizedText)) return [];

        var lines = FindLines(normalizedText, rules);
        var firstTitle = lines.FindIndex(line => line.IsTitle);
        if (firstTitle < 0 && !splitOnBlankLines)
        {
            return [new BookImportChapter(0, 0, "全文", 0, normalizedText.Length)];
        }

        var markers = new List<(int TitleOffset, int ContentOffset, string? Title)>();
        var startIndex = firstTitle >= 0 ? firstTitle : lines.FindIndex(line => !line.IsBlank);
        var hasBody = false;
        var pendingBlank = false;
        for (var index = startIndex; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.IsTitle)
            {
                markers.Add((line.Start, line.End, line.Text.Trim()));
                hasBody = false;
                pendingBlank = false;
            }
            else if (line.IsBlank)
            {
                pendingBlank |= hasBody && splitOnBlankLines;
            }
            else
            {
                if (markers.Count == 0 || pendingBlank)
                {
                    markers.Add((line.Start, line.Start, null));
                    hasBody = false;
                }

                hasBody = true;
                pendingBlank = false;
            }
        }

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

    private static List<Line> FindLines(string normalizedText, IReadOnlyList<ChapterRule> rules)
    {
        var lines = new List<Line>();
        var orderedRules = rules.Where(rule => rule.IsEnabled)
            .OrderBy(rule => rule.SortOrder)
            .Select(rule => ChapterRuleRegexPolicy.Create(rule.Pattern))
            .ToArray();
        var lineStart = 0;
        foreach (var line in normalizedText.Split('\n'))
        {
            var end = Math.Min(lineStart + line.Length + 1, normalizedText.Length);
            lines.Add(new Line(
                lineStart,
                end,
                line,
                string.IsNullOrWhiteSpace(line),
                orderedRules.Any(regex => regex.IsMatch(line))));
            lineStart = end;
        }

        return lines;
    }

    private sealed record Line(int Start, int End, string Text, bool IsBlank, bool IsTitle);
}
