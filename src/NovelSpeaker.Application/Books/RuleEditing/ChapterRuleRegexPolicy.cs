using System.Text.RegularExpressions;

namespace NovelSpeaker.Application.Books.RuleEditing;

/// <summary>Defines the bounded execution policy shared by chapter-rule editing and import.</summary>
public static class ChapterRuleRegexPolicy
{
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public static Regex Create(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant, MatchTimeout);
}
