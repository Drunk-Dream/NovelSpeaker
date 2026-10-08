using System.Text.RegularExpressions;

namespace NovelSpeaker.Application.Books.RuleEditing;

internal static class RulePatternValidation
{
    public static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    public static void Validate(string pattern, TimeSpan? timeout = null)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, timeout ?? ChapterRuleRegexPolicy.MatchTimeout);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"正则表达式无效：{exception.Message}", exception);
        }
    }

    public static bool IsValid(string pattern, TimeSpan timeout)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, timeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string Summarize(string pattern)
    {
        var summary = Regex.Replace(pattern.Trim(), @"\s+", " ");
        return summary.Length <= 80 ? summary : $"{summary[..77]}...";
    }
}
