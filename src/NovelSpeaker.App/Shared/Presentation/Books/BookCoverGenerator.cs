using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace NovelSpeaker.App.Shared.Presentation.Books;

public sealed partial class BookCoverGenerator : IBookCoverGenerator
{
    private static readonly PalettePreset[] Palettes =
    [
        new(0, Color.FromRgb(30, 58, 138), Color.FromRgb(37, 99, 235), Color.FromArgb(88, 191, 219, 254), BookCoverForegroundTone.Light),
        new(1, Color.FromRgb(22, 101, 52), Color.FromRgb(74, 222, 128), Color.FromArgb(96, 220, 252, 231), BookCoverForegroundTone.Light),
        new(2, Color.FromRgb(127, 29, 29), Color.FromRgb(249, 115, 22), Color.FromArgb(92, 254, 215, 170), BookCoverForegroundTone.Light),
        new(3, Color.FromRgb(76, 29, 149), Color.FromRgb(168, 85, 247), Color.FromArgb(96, 221, 214, 254), BookCoverForegroundTone.Light),
        new(4, Color.FromRgb(226, 232, 240), Color.FromRgb(148, 163, 184), Color.FromArgb(100, 71, 85, 105), BookCoverForegroundTone.Dark),
        new(5, Color.FromRgb(252, 211, 77), Color.FromRgb(245, 158, 11), Color.FromArgb(96, 120, 53, 15), BookCoverForegroundTone.Dark)
    ];

    private const string DefaultTitle = "未命名书籍";

    public GeneratedBookCover Generate(string title)
    {
        var normalizedDisplayTitle = NormalizeDisplayTitle(title);
        var normalizedTitleKey = NormalizeTitleKey(normalizedDisplayTitle);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedTitleKey));
        var palette = Palettes[hash[0] % Palettes.Length];
        var decorationPresetId = hash[1] % 4;

        return new GeneratedBookCover(
            normalizedTitleKey,
            BuildDisplayLines(normalizedDisplayTitle),
            palette.Id,
            decorationPresetId,
            palette.ForegroundTone,
            palette.StartColor,
            palette.EndColor,
            palette.AccentColor);
    }

    internal static string NormalizeDisplayTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return DefaultTitle;
        }

        var collapsed = WhitespaceRegex().Replace(title.Trim(), " ");
        return string.IsNullOrWhiteSpace(collapsed)
            ? DefaultTitle
            : collapsed;
    }

    internal static string NormalizeTitleKey(string title)
    {
        return NormalizeDisplayTitle(title).ToUpper(CultureInfo.InvariantCulture);
    }

    internal static IReadOnlyList<string> BuildDisplayLines(string title)
    {
        var normalized = NormalizeDisplayTitle(title);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 1
            ? BuildWordLines(tokens)
            : BuildCharacterLines(normalized);
    }

    private static IReadOnlyList<string> BuildWordLines(IReadOnlyList<string> tokens)
    {
        const int maxLineLength = 8;
        var lines = new List<string>(capacity: 3);
        var currentLine = new StringBuilder();
        var wasTruncated = false;

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var candidateLength = currentLine.Length == 0
                ? token.Length
                : currentLine.Length + 1 + token.Length;

            if (candidateLength <= maxLineLength || currentLine.Length == 0)
            {
                if (currentLine.Length > 0)
                {
                    currentLine.Append(' ');
                }

                currentLine.Append(token);
                continue;
            }

            lines.Add(currentLine.ToString());
            currentLine.Clear();

            if (lines.Count == 2)
            {
                currentLine.Append(token);
                wasTruncated = index < tokens.Count - 1;
                break;
            }

            currentLine.Append(token);
        }

        if (currentLine.Length > 0)
        {
            lines.Add(currentLine.ToString());
        }

        while (lines.Count > 3)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0)
        {
            lines.Add(DefaultTitle);
        }

        if (lines.Count == 3)
        {
            lines[2] = wasTruncated
                ? AppendEllipsis(lines[2], maxLineLength)
                : AppendEllipsisIfNeeded(lines[2], maxLineLength);
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildCharacterLines(string title)
    {
        const int maxLineLength = 6;
        var lines = new List<string>(capacity: 3);

        for (var offset = 0; offset < title.Length && lines.Count < 3; offset += maxLineLength)
        {
            var remaining = title.Length - offset;
            if (lines.Count == 2 && remaining > maxLineLength)
            {
                lines.Add($"{title.Substring(offset, maxLineLength - 3)}...");
                return lines;
            }

            lines.Add(title.Substring(offset, Math.Min(maxLineLength, remaining)));
        }

        if (lines.Count == 0)
        {
            lines.Add(DefaultTitle);
        }

        return lines;
    }

    private static string AppendEllipsisIfNeeded(string line, int maxLineLength)
    {
        if (line.Length <= maxLineLength)
        {
            return line;
        }

        return AppendEllipsis(line, maxLineLength);
    }

    private static string AppendEllipsis(string line, int maxLineLength)
    {
        return $"{line.Substring(0, Math.Max(1, maxLineLength - 3))}...";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed record PalettePreset(
        int Id,
        Color StartColor,
        Color EndColor,
        Color AccentColor,
        BookCoverForegroundTone ForegroundTone);
}
