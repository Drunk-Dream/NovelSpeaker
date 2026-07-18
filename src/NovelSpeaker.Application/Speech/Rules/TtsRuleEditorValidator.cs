using System.Text.RegularExpressions;
using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Application.Speech.Rules;

internal static partial class TtsRuleEditorValidator
{
    internal const string UnsupportedCookieLoginInfoMessage =
        "当前版本不支持 Cookie/LoginInfo；请移除相关字段、Header 和模板表达式。";

    public static IReadOnlyList<string> Validate(TtsRuleEditorModel editor)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(editor.Name))
        {
            errors.Add("规则名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(editor.Url))
        {
            errors.Add("规则 URL 不能为空。");
        }
        else
        {
            ValidateTemplate(editor.Url, "URL", errors);
        }

        if (!string.IsNullOrWhiteSpace(editor.ConcurrentRate) && !ConcurrentRatePattern().IsMatch(editor.ConcurrentRate))
        {
            errors.Add("并发限制必须使用类似 2/1000 的格式。");
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in editor.Headers)
        {
            if (!keys.Add(header.Key))
            {
                errors.Add($"Header 中存在重复键：{header.Key}");
            }

            ValidateTemplate(header.Value, $"Header {header.Key}", errors);
        }

        if (editor.RequestOptions.Method is not null and not ("GET" or "POST"))
        {
            errors.Add("requestOptions.method 仅支持 GET 或 POST。");
        }

        if (editor.RequestOptions.Method == "GET" && !string.IsNullOrWhiteSpace(editor.RequestOptions.Body))
        {
            errors.Add("GET 请求不能携带 body。");
        }

        ValidateTemplate(editor.RequestOptions.Body, "requestOptions.body", errors);
        if (HasUnsupportedDependency(editor))
        {
            errors.Add(UnsupportedCookieLoginInfoMessage);
        }

        return errors;
    }

    private static bool HasUnsupportedDependency(TtsRuleEditorModel editor) =>
        ContainsUnsupportedReference(editor.Url) ||
        editor.Headers.Any(header => header.Key.Trim().Equals("Cookie", StringComparison.OrdinalIgnoreCase) || ContainsUnsupportedReference(header.Value)) ||
        ContainsUnsupportedReference(editor.RequestOptions.Body);

    private static bool ContainsUnsupportedReference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            return NormalizedTemplate.Parse(text).Segments.OfType<ExpressionTemplateSegment>()
                .Any(segment => UnsupportedReferencePattern().IsMatch(segment.Expression));
        }
        catch (FormatException)
        {
            return UnsupportedReferencePattern().IsMatch(text);
        }
    }

    private static void ValidateTemplate(string? text, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            _ = NormalizedTemplate.Parse(text);
        }
        catch (FormatException)
        {
            errors.Add($"{field} 模板格式无效，请检查模板语法。");
        }
    }

    [GeneratedRegex(@"^\d+/\d+$")]
    private static partial Regex ConcurrentRatePattern();

    [GeneratedRegex(@"\b(?:cookie|loginInfo)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedReferencePattern();
}
