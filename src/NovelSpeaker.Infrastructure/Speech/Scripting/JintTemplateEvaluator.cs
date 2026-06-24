using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Scripting;

/// <summary>
/// Evaluates converted templates inside a restricted in-process Jint engine.
/// </summary>
public sealed partial class JintTemplateEvaluator : ITemplateEvaluator
{
    private const int MaxStatements = 256;
    private const int MaxRecursionDepth = 32;
    private const int MaxOutputLength = 8192;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    public Task<string> EvaluateAsync(
        NormalizedTemplate template,
        TtsRuleContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvaluateCore(template, context, cancellationToken));
    }

    public async Task<TtsRequestPreview> CreatePreviewAsync(
        HttpTtsRule rule,
        TtsRuleContext context,
        CancellationToken cancellationToken)
    {
        var normalizedRule = rule.ToNormalizedRule();
        var url = await EvaluateAsync(normalizedRule.UrlTemplate, context, cancellationToken);
        var header = normalizedRule.HeaderTemplate is null
            ? null
            : await EvaluateAsync(normalizedRule.HeaderTemplate, context, cancellationToken);
        var requestOptions = normalizedRule.RequestOptionsTemplate is null
            ? null
            : await EvaluateAsync(normalizedRule.RequestOptionsTemplate, context, cancellationToken);

        return new TtsRequestPreview(
            RedactUrl(url),
            RedactJsonLikeText(header),
            RedactJsonLikeText(requestOptions));
    }

    private static string EvaluateCore(
        NormalizedTemplate template,
        TtsRuleContext context,
        CancellationToken cancellationToken)
    {
        var engine = CreateEngine(context);
        var builder = new StringBuilder();

        foreach (var segment in template.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (segment)
            {
                case LiteralTemplateSegment literal:
                    builder.Append(literal.Text);
                    break;
                case ExpressionTemplateSegment expression:
                    builder.Append(EvaluateExpression(engine, expression.Expression));
                    break;
            }

            if (builder.Length > MaxOutputLength)
            {
                throw new InvalidOperationException("模板求值结果超过允许的最大长度。");
            }
        }

        return builder.ToString();
    }

    private static Engine CreateEngine(TtsRuleContext context)
    {
        var engine = new Engine(options =>
        {
            options.Strict();
            options.TimeoutInterval(Timeout);
            options.MaxStatements(MaxStatements);
            options.LimitRecursion(MaxRecursionDepth);
        });

        var setupScript =
            $$"""
            const speakText = {{JsonSerializer.Serialize(context.SpeakText)}};
            const speakSpeed = {{context.SpeakSpeed}};
            const loginInfo = Object.freeze({{JsonSerializer.Serialize(context.LoginInfo)}});
            const source = Object.freeze({
              name: {{JsonSerializer.Serialize(context.Source.Name)}},
              url: {{JsonSerializer.Serialize(context.Source.Url)}},
              contentType: {{SerializeNullableString(context.Source.ContentType)}},
              concurrentRate: {{SerializeNullableString(context.Source.ConcurrentRate)}},
              enabledCookieJar: {{context.Source.EnabledCookieJar.ToString().ToLowerInvariant()}},
              getLoginInfo() { return loginInfo; },
              getLoginInfoMap() { return loginInfo; }
            });
            const java = Object.freeze({
              encodeURI(value) { return encodeURI(value == null ? "" : String(value)); },
              encodeURIComponent(value) { return encodeURIComponent(value == null ? "" : String(value)); }
            });
            """;

        engine.Execute(setupScript);
        return engine;
    }

    private static string EvaluateExpression(Engine engine, string expression)
    {
        var value = engine.Evaluate($"({expression})");
        if (value.IsNull() || value.IsUndefined())
        {
            return string.Empty;
        }

        if (value.IsObject())
        {
            engine.SetValue("__novelSpeakerResult", value);
            var jsonValue = engine.Evaluate("JSON.stringify(__novelSpeakerResult)");
            if (jsonValue.IsNull() || jsonValue.IsUndefined())
            {
                return string.Empty;
            }

            var objectText = jsonValue.ToString();
            return objectText.Length > MaxOutputLength
                ? throw new InvalidOperationException("模板对象结果超过允许的最大长度。")
                : objectText;
        }

        var text = value.ToString();
        return text.Length > MaxOutputLength
            ? throw new InvalidOperationException("模板表达式结果超过允许的最大长度。")
            : text;
    }

    private static string SerializeNullableString(string? value)
    {
        return value is null ? "null" : JsonSerializer.Serialize(value);
    }

    private static string RedactUrl(string url)
    {
        var separatorIndex = url.IndexOf('?', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == url.Length - 1)
        {
            return RedactBearerTokens(url);
        }

        var prefix = url[..(separatorIndex + 1)];
        var query = url[(separatorIndex + 1)..];
        var redactedPairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var equalsIndex = pair.IndexOf('=', StringComparison.Ordinal);
                if (equalsIndex < 0)
                {
                    return pair;
                }

                var key = pair[..equalsIndex];
                return IsSensitiveKey(key)
                    ? $"{key}=***"
                    : pair;
            });

        return RedactBearerTokens(prefix + string.Join("&", redactedPairs));
    }

    private static string? RedactJsonLikeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            WriteRedactedElement(writer, null, document.RootElement);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return RedactBearerTokens(value);
        }
    }

    private static void WriteRedactedElement(Utf8JsonWriter writer, string? propertyName, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteRedactedElement(writer, property.Name, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteRedactedElement(writer, propertyName, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String when propertyName is not null && IsSensitiveKey(propertyName):
                writer.WriteStringValue("***");
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        return key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("login", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactBearerTokens(string input)
    {
        return BearerPattern().Replace(input, "$1***");
    }

    [GeneratedRegex("(Bearer\\s+)[^\\s\"&]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();
}
