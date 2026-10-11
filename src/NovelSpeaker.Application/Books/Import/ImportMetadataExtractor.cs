using System.Text.RegularExpressions;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books.Import;

/// <summary>
/// Projects import metadata without changing the normalized source text.
/// </summary>
public sealed class ImportMetadataExtractor
{
    private const int HeaderPrefixCharacterLimit = 8192;
    private const int HeaderPrefixLineLimit = 80;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly string[] SupportedGroups = ["name", "author", "description"];

    public ImportMetadata Extract(
        string sourceNameWithoutExtension,
        string normalizedText,
        int? firstExplicitTitleOffset,
        IReadOnlyList<FileNameMetadataRule> fileNameRules,
        IReadOnlyList<TextHeaderMetadataRule> textHeaderRules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(normalizedText);

        var result = new Captures();
        foreach (var rule in fileNameRules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.SortOrder))
        {
            if (TryExtract(sourceNameWithoutExtension, rule.Pattern, multiline: false, out var captures))
            {
                result = captures;
                break;
            }
        }

        var headerLength = firstExplicitTitleOffset is { } offset
            ? Math.Clamp(offset, 0, normalizedText.Length)
            : GetBoundedPrefixLength(normalizedText);
        var header = normalizedText[..headerLength];
        foreach (var rule in textHeaderRules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.SortOrder))
        {
            if (TryExtract(header, rule.Pattern, multiline: true, out var captures))
            {
                result = result.FillMissing(captures);
            }
        }

        return new ImportMetadata(result.Name ?? sourceNameWithoutExtension, result.Author, result.Description,
            result.Name is not null, result.Author is not null);
    }

    public static void ValidatePattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline, MatchTimeout);
        if (!regex.GetGroupNames().Intersect(SupportedGroups, StringComparer.Ordinal).Any())
        {
            throw new ArgumentException("规则必须包含 name、author 或 description 命名组。", nameof(pattern));
        }
    }

    private static bool TryExtract(string input, string pattern, bool multiline, out Captures captures)
    {
        captures = new Captures();
        try
        {
            var regex = new Regex(pattern,
                RegexOptions.CultureInvariant | (multiline ? RegexOptions.Multiline : RegexOptions.None),
                MatchTimeout);
            if (!regex.GetGroupNames().Intersect(SupportedGroups, StringComparer.Ordinal).Any())
            {
                return false;
            }

            for (var match = regex.Match(input); match.Success; match = match.NextMatch())
            {
                captures = new Captures(
                    Read(match, "name"),
                    Read(match, "author"),
                    Read(match, "description"));
                if (captures.HasValue)
                {
                    return true;
                }
            }

            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string? Read(Match match, string groupName)
    {
        var value = match.Groups[groupName].Value.Trim();
        return value.Length == 0 ? null : value;
    }

    private static int GetBoundedPrefixLength(string text)
    {
        var limit = Math.Min(HeaderPrefixCharacterLimit, text.Length);
        var lines = 0;
        for (var index = 0; index < limit; index++)
        {
            if (text[index] == '\n' && ++lines >= HeaderPrefixLineLimit)
            {
                return index + 1;
            }
        }

        return limit;
    }

    private sealed record Captures(string? Name = null, string? Author = null, string? Description = null)
    {
        public bool HasValue => Name is not null || Author is not null || Description is not null;

        public Captures FillMissing(Captures other) =>
            new(Name ?? other.Name, Author ?? other.Author, Description ?? other.Description);
    }
}
