using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

/// <summary>
/// Converts imported Legado-style rules into the application's canonical rule format.
/// </summary>
public sealed partial class LegadoRuleConverter : ITtsRuleConverter
{
    private static readonly HashSet<string> SupportedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "url",
        "contentType",
        "concurrentRate",
        "header",
        "enabledCookieJar",
        "isEnabled",
        "lastUpdateTime"
    };

    public TtsRuleConversionResult Convert(JsonElement ruleElement)
    {
        var blockingIssues = new List<string>();
        var unsupportedFields = ruleElement.EnumerateObject()
            .Where(property => !SupportedFields.Contains(property.Name))
            .Select(property => property.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var name = ReadOptionalString(ruleElement, "name");
        var rawUrl = ReadOptionalString(ruleElement, "url");

        if (string.IsNullOrWhiteSpace(name))
        {
            blockingIssues.Add("缺少必需字段：name。");
        }

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            blockingIssues.Add("缺少必需字段：url。");
        }

        var split = ExtractRequestOptions(rawUrl);
        if (split.ErrorMessage is not null)
        {
            blockingIssues.Add(split.ErrorMessage);
        }

        var normalizedUrl = NormalizeTemplate(split.Url ?? rawUrl, "url", blockingIssues);
        var normalizedHeader = NormalizeTemplate(ReadOptionalString(ruleElement, "header"), "header", blockingIssues);
        var normalizedRequestOptions = NormalizeTemplate(split.RequestOptionsJson, "requestOptions", blockingIssues);

        var candidateRule = CreateCandidateRule(
            ruleElement,
            name,
            normalizedUrl,
            normalizedHeader,
            normalizedRequestOptions,
            unsupportedFields);
        candidateRule = ApplyCompatibilityStatus(candidateRule, blockingIssues, unsupportedFields);

        return new TtsRuleConversionResult(candidateRule, unsupportedFields, blockingIssues);
    }

    private static (string? Url, string? RequestOptionsJson, string? ErrorMessage) ExtractRequestOptions(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return (rawUrl, null, null);
        }

        var markerIndex = rawUrl.LastIndexOf(",{", StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return (rawUrl, null, null);
        }

        var baseUrl = rawUrl[..markerIndex].Trim();
        var requestOptionsJson = rawUrl[(markerIndex + 1)..].Trim();

        try
        {
            using var document = JsonDocument.Parse(requestOptionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (rawUrl, null, "URL 附加请求配置必须是 JSON 对象。");
            }

            return (baseUrl, requestOptionsJson, null);
        }
        catch (JsonException)
        {
            return (rawUrl, null, "URL 附加请求配置不是有效的 JSON 对象。");
        }
    }

    private static string? NormalizeTemplate(string? templateText, string fieldName, List<string> blockingIssues)
    {
        if (string.IsNullOrWhiteSpace(templateText))
        {
            return templateText;
        }

        try
        {
            var parsed = NormalizedTemplate.Parse(templateText);
            var builder = new StringBuilder();

            foreach (var segment in parsed.Segments)
            {
                switch (segment)
                {
                    case LiteralTemplateSegment literal:
                        builder.Append(literal.Text);
                        break;
                    case ExpressionTemplateSegment expression:
                        var normalizedExpression = NormalizeExpression(expression.Expression);
                        if (normalizedExpression is null)
                        {
                            blockingIssues.Add($"字段 {fieldName} 中包含当前版本无法转换的表达式：{expression.Expression}");
                            normalizedExpression = expression.Expression;
                        }

                        builder.Append("{{");
                        builder.Append(normalizedExpression);
                        builder.Append("}}");
                        break;
                }
            }

            return builder.ToString();
        }
        catch (FormatException exception)
        {
            blockingIssues.Add($"字段 {fieldName} 的模板格式无效：{exception.Message}");
            return templateText;
        }
    }

    private static string? NormalizeExpression(string expression)
    {
        if (CookiePattern().IsMatch(expression))
        {
            return null;
        }

        var normalized = JavaEncodeUriPattern().Replace(expression, "encodeURI(");
        normalized = JavaEncodeURIComponentPattern().Replace(normalized, "encodeURIComponent(");
        normalized = SourceLoginInfoPattern().Replace(normalized, "loginInfo");

        if (UnsupportedJavaPattern().IsMatch(normalized))
        {
            return null;
        }

        return UnsupportedSourcePattern().IsMatch(normalized)
            ? null
            : normalized;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => property.Value.GetRawText()
            };
        }

        return null;
    }

    private static bool ReadOptionalBoolean(JsonElement root, string propertyName, bool defaultValue = false)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.Value.GetString(), out var parsed) => parsed,
                _ => defaultValue
            };
        }

        return defaultValue;
    }

    private static long? ReadOptionalInt64(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var number))
            {
                return number;
            }

            if (property.Value.ValueKind == JsonValueKind.String &&
                long.TryParse(property.Value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static HttpTtsRule CreateCandidateRule(
        JsonElement ruleElement,
        string? name,
        string? normalizedUrl,
        string? normalizedHeader,
        string? normalizedRequestOptions,
        IReadOnlyList<string> unsupportedFields)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        return new HttpTtsRule(
            0,
            name ?? string.Empty,
            normalizedUrl ?? string.Empty,
            ReadOptionalString(ruleElement, "contentType"),
            ReadOptionalString(ruleElement, "concurrentRate"),
            normalizedHeader,
            normalizedRequestOptions,
            ReadOptionalBoolean(ruleElement, "enabledCookieJar"),
            ReadOptionalInt64(ruleElement, "lastUpdateTime"),
            string.Empty,
            ReadOptionalBoolean(ruleElement, "isEnabled", defaultValue: true),
            TtsRuleCompatibilityStatus.Compatible,
            unsupportedFields,
            null,
            utcNow,
            utcNow);
    }

    private static HttpTtsRule ApplyCompatibilityStatus(
        HttpTtsRule candidateRule,
        IReadOnlyList<string> blockingIssues,
        IReadOnlyList<string> unsupportedFields)
    {
        return candidateRule with
        {
            RuleJson = NovelSpeakerRuleJsonSerializer.Serialize(candidateRule),
            CompatibilityStatus = blockingIssues.Count > 0
                ? TtsRuleCompatibilityStatus.NeedsManualAdjustment
                : unsupportedFields.Count == 0
                    ? TtsRuleCompatibilityStatus.Compatible
                    : TtsRuleCompatibilityStatus.CompatibleWithWarnings
        };
    }

    [GeneratedRegex(@"\bcookie\s*\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CookiePattern();

    [GeneratedRegex(@"\bjava\s*\.\s*encodeURI\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaEncodeUriPattern();

    [GeneratedRegex(@"\bjava\s*\.\s*encodeURIComponent\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaEncodeURIComponentPattern();

    [GeneratedRegex(@"\bsource\s*\.\s*getLoginInfo(Map)?\s*\(\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceLoginInfoPattern();

    [GeneratedRegex(@"\bjava\s*\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedJavaPattern();

    [GeneratedRegex(@"\bsource\s*\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedSourcePattern();
}
