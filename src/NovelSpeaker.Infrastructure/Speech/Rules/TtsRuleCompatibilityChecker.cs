using System.Text.Json;
using System.Text.RegularExpressions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal static partial class TtsRuleCompatibilityChecker
{
    public const string UnsupportedCookieLoginInfoMessage =
        "当前版本不支持 Cookie/LoginInfo；请移除相关字段、Header 和模板表达式。";

    public static bool HasUnsupportedImportDependency(
        JsonElement ruleElement,
        string? url,
        string? header,
        string? requestOptionsJson)
    {
        return HasProperty(ruleElement, "enabledCookieJar") ||
               HasProperty(ruleElement, "loginInfo") ||
               ContainsUnsupportedTemplateReference(url) ||
               ContainsUnsupportedTemplateReference(header) ||
               ContainsUnsupportedTemplateReference(requestOptionsJson) ||
               ContainsCookieHeader(header) ||
               ContainsCookieHeaderInRequestOptions(requestOptionsJson);
    }

    public static bool HasUnsupportedEditorDependency(TtsRuleEditorModel editor)
    {
        return ContainsUnsupportedTemplateReference(editor.Url) ||
               editor.Headers.Any(header =>
                   IsCookieHeader(header.Key) || ContainsUnsupportedTemplateReference(header.Value)) ||
               ContainsUnsupportedTemplateReference(editor.RequestOptions.Body);
    }

    public static bool HasUnsupportedRuntimeDependency(NormalizedHttpTtsRule rule)
    {
        return ContainsUnsupportedTemplateReference(rule.UrlTemplate.RawText) ||
               ContainsUnsupportedTemplateReference(rule.HeaderTemplate?.RawText) ||
               ContainsUnsupportedTemplateReference(rule.RequestOptionsTemplate?.RawText) ||
               ContainsCookieHeader(rule.HeaderTemplate?.RawText) ||
               ContainsCookieHeaderInRequestOptions(rule.RequestOptionsTemplate?.RawText);
    }

    public static bool ContainsCookieHeader(IEnumerable<KeyValuePair<string, string>> headers)
    {
        return headers.Any(header => IsCookieHeader(header.Key));
    }

    public static bool ContainsUnsupportedTemplateReference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            return NormalizedTemplate.Parse(text).Segments
                .OfType<ExpressionTemplateSegment>()
                .Any(segment => IsUnsupportedExpression(segment.Expression));
        }
        catch (FormatException)
        {
            return UnsupportedReferencePattern().IsMatch(text);
        }
    }

    public static bool IsUnsupportedExpression(string expression)
    {
        return UnsupportedReferencePattern().IsMatch(expression);
    }

    private static bool ContainsCookieHeader(string? headerJson)
    {
        if (string.IsNullOrWhiteSpace(headerJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(headerJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.EnumerateObject().Any(property => IsCookieHeader(property.Name));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsCookieHeaderInRequestOptions(string? requestOptionsJson)
    {
        if (string.IsNullOrWhiteSpace(requestOptionsJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(requestOptionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(document.RootElement, "headers", out var headers) ||
                headers.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return headers.EnumerateObject().Any(property => IsCookieHeader(property.Name));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.EnumerateObject().Any(property =>
                   property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsCookieHeader(string key)
    {
        return key.Trim().Equals("Cookie", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(
        @"\b(?:cookie|loginInfo)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedReferencePattern();
}
