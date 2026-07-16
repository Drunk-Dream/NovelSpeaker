using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

/// <summary>
/// Converts imported Legado-style rules into the application's canonical rule format.
/// </summary>
public sealed partial class LegadoRuleConverter
{
    private readonly TimeProvider _timeProvider;

    public LegadoRuleConverter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private static readonly HashSet<string> SupportedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "url",
        "contentType",
        "concurrentRate",
        "header",
        "isEnabled",
        "lastUpdateTime"
    };

    public TtsRuleConversionResult Convert(JsonElement ruleElement)
    {
        var source = LegadoRuleSourceDto.FromJson(ruleElement, SupportedFields);
        var blockingIssues = new List<string>();
        var name = source.Name;
        var rawUrl = source.Url;
        var rawHeader = source.Header;

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

        if (TtsRuleCompatibilityChecker.HasUnsupportedImportDependency(
                source.Element,
                rawUrl,
                rawHeader,
                split.RequestOptionsJson))
        {
            blockingIssues.Add(TtsRuleCompatibilityChecker.UnsupportedCookieLoginInfoMessage);
        }

        var normalizedUrl = NormalizeTemplate(split.Url ?? rawUrl, "url", blockingIssues);
        var normalizedHeader = NormalizeTemplate(rawHeader, "header", blockingIssues);
        var normalizedRequestOptions = StripUnsupportedRequestOptions(
            NormalizeTemplate(split.RequestOptionsJson, "requestOptions", blockingIssues));

        var candidateRule = CreateCandidateRule(
            source,
            name,
            normalizedUrl,
            normalizedHeader,
            normalizedRequestOptions);

        return new TtsRuleConversionResult(candidateRule, source.UnsupportedFields, blockingIssues);
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
                        if (TtsRuleCompatibilityChecker.IsUnsupportedExpression(expression.Expression))
                        {
                            builder.Append("{{");
                            builder.Append(expression.Expression);
                            builder.Append("}}");
                            break;
                        }

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
        catch (FormatException)
        {
            blockingIssues.Add($"字段 {fieldName} 的模板格式无效，请检查模板语法。");
            return templateText;
        }
    }

    private static string? NormalizeExpression(string expression)
    {
        var normalized = JavaEncodeUriPattern().Replace(expression, "encodeURI(");
        normalized = JavaEncodeURIComponentPattern().Replace(normalized, "encodeURIComponent(");
        if (UnsupportedJavaPattern().IsMatch(normalized))
        {
            return null;
        }

        return UnsupportedSourcePattern().IsMatch(normalized)
            ? null
            : normalized;
    }

    private HttpTtsRule CreateCandidateRule(
        LegadoRuleSourceDto source,
        string? name,
        string? normalizedUrl,
        string? normalizedHeader,
        string? normalizedRequestOptions)
    {
        var utcNow = _timeProvider.GetUtcNow();
        var headers = TtsRulePersistenceMapper.ParseHeaders(normalizedHeader);
        return new HttpTtsRule(
            0,
            name ?? string.Empty,
            normalizedUrl ?? string.Empty,
            source.ContentType,
            source.ConcurrentRate,
            headers,
            TtsRulePersistenceMapper.ParseRequestMethod(normalizedRequestOptions),
            TtsRulePersistenceMapper.ParseRequestBody(normalizedRequestOptions),
            TtsRulePersistenceMapper.IsRequestBodyJsonStructure(normalizedRequestOptions),
            source.LastUpdateTime,
            source.IsEnabled,
            null,
            utcNow,
            utcNow);
    }

    private static string? StripUnsupportedRequestOptions(string? requestOptionsJson)
    {
        if (string.IsNullOrWhiteSpace(requestOptionsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(requestOptionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            writer.WriteStartObject();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("method") || property.NameEquals("body"))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
            writer.Flush();
            var normalized = Encoding.UTF8.GetString(stream.ToArray());
            return normalized == "{}" ? null : normalized;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\bjava\s*\.\s*encodeURI\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaEncodeUriPattern();

    [GeneratedRegex(@"\bjava\s*\.\s*encodeURIComponent\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaEncodeURIComponentPattern();

    [GeneratedRegex(@"\bjava\s*\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedJavaPattern();

    [GeneratedRegex(@"\bsource\s*\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedSourcePattern();
}
