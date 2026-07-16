using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Speech.Rules;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Evaluates rule templates and normalizes the result into a concrete HTTP request shape.
/// </summary>
public sealed class TtsRequestCompiler : ITtsRequestCompiler
{
    private static readonly IReadOnlyDictionary<string, string> DefaultHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "*/*",
            ["User-Agent"] = "NovelSpeaker/1.0"
        };

    private readonly ITemplateEvaluator _templateEvaluator;
    private readonly ILogger<TtsRequestCompiler> _logger;

    public TtsRequestCompiler(
        ITemplateEvaluator templateEvaluator,
        ILogger<TtsRequestCompiler>? logger = null)
    {
        _templateEvaluator = templateEvaluator;
        _logger = logger ?? NullLogger<TtsRequestCompiler>.Instance;
    }

    public async Task<TtsRequestCompilationResult> CompileAsync(
        NormalizedHttpTtsRule rule,
        TtsRuleContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TtsRuleCompatibilityChecker.HasUnsupportedRuntimeDependency(rule))
        {
            return Failure(
                TtsErrorKind.InvalidRule,
                TtsRuleCompatibilityChecker.UnsupportedCookieLoginInfoMessage);
        }

        string urlText;
        var evaluatedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? requestBodyText;
        try
        {
            urlText = await _templateEvaluator.EvaluateAsync(rule.UrlTemplate, context, cancellationToken);
            foreach (var header in rule.HeaderTemplates)
            {
                evaluatedHeaders[header.Key] = await _templateEvaluator.EvaluateAsync(header.Value, context, cancellationToken);
            }

            requestBodyText = rule.RequestBodyTemplate is null
                ? null
                : await _templateEvaluator.EvaluateAsync(rule.RequestBodyTemplate, context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SensitiveFailureLogger.LogError(
                _logger,
                "TTS request template evaluation",
                exception,
                EnumerateKnownSecrets(rule, context));
            return Failure(TtsErrorKind.ScriptError, "模板求值失败，请检查规则模板后重试。");
        }

        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url))
        {
            return Failure(TtsErrorKind.InvalidRule, "规则 URL 不是有效的绝对地址。");
        }

        var bodyElementResult = ParseBody(requestBodyText, rule.RequestBodyIsJsonStructure);
        if (!bodyElementResult.IsSuccess)
        {
            return Failure(TtsErrorKind.InvalidRule, bodyElementResult.Message!);
        }

        var method = string.IsNullOrWhiteSpace(rule.RequestMethod)
            ? bodyElementResult.BodyElement is null ? "GET" : "POST"
            : rule.RequestMethod.Trim().ToUpperInvariant();
        if (method is not ("GET" or "POST"))
        {
            return Failure(TtsErrorKind.InvalidRule, $"当前版本仅支持 GET 和 POST，请求方法 {method} 不受支持。");
        }

        if (method == "GET" && bodyElementResult.BodyElement is not null)
        {
            return Failure(TtsErrorKind.InvalidRule, "GET 请求不能携带 body。");
        }

        var headers = MergeHeaders(DefaultHeaders, evaluatedHeaders);
        if (TtsRuleCompatibilityChecker.ContainsCookieHeader(headers))
        {
            return Failure(
                TtsErrorKind.InvalidRule,
                TtsRuleCompatibilityChecker.UnsupportedCookieLoginInfoMessage);
        }

        var bodyResult = BuildBody(bodyElementResult.BodyElement, headers);
        if (!bodyResult.IsSuccess)
        {
            return Failure(bodyResult.Kind!.Value, bodyResult.Message!);
        }

        var request = new ParsedTtsRequest(
            rule.RuleId,
            method,
            url,
            headers,
            bodyResult.Body!,
            rule.DeclaredContentType);

        var preview = new TtsRequestPreview(
            request.Method,
            SensitiveDataRedactor.RedactUrl(request.Url.ToString()),
            SensitiveDataRedactor.SerializeRedactedDictionary(request.Headers),
            BuildBodyPreview(request.Body),
            request.DeclaredContentType);

        return new TtsRequestCompilationResult(request, preview, Array.Empty<string>(), null);
    }

    private static IEnumerable<string?> EnumerateKnownSecrets(
        NormalizedHttpTtsRule rule,
        TtsRuleContext context)
    {
        yield return rule.UrlTemplate.RawText;
        yield return rule.RequestBodyTemplate?.RawText;
        yield return context.SpeakText;

        foreach (var header in rule.HeaderTemplates)
        {
            yield return header.Key;
            yield return header.Value.RawText;
        }
    }

    private static TtsRequestCompilationResult Failure(TtsErrorKind kind, string message)
    {
        return new TtsRequestCompilationResult(
            null,
            null,
            Array.Empty<string>(),
            new TtsExecutionFailure(kind, message, null, null, null, null));
    }

    private static BodyParseResult ParseBody(string? text, bool isJsonStructure)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BodyParseResult.Success(null);
        }

        if (isJsonStructure)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return BodyParseResult.Success(document.RootElement.Clone());
            }
            catch (JsonException)
            {
                return BodyParseResult.Error("POST JSON 的 body 不是有效的 JSON。");
            }
        }

        using var stringDocument = JsonDocument.Parse(JsonSerializer.Serialize(text));
        return BodyParseResult.Success(stringDocument.RootElement.Clone());
    }

    private static BodyBuildResult BuildBody(JsonElement? bodyElement, IReadOnlyDictionary<string, string> headers)
    {
        if (bodyElement is null || bodyElement.Value.ValueKind == JsonValueKind.Null)
        {
            return BodyBuildResult.Success(ParsedTtsRequestBody.None);
        }

        var contentType = TryGetHeader(headers, "Content-Type");
        if (IsJsonContentType(contentType) || bodyElement.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return BodyBuildResult.Success(new ParsedTtsRequestBody(
                ParsedTtsRequestBodyKind.Json,
                bodyElement.Value.ValueKind == JsonValueKind.String
                    ? bodyElement.Value.GetString()
                    : bodyElement.Value.GetRawText(),
                null));
        }

        if (IsFormContentType(contentType))
        {
            var formFields = BuildFormFields(bodyElement.Value);
            if (formFields is null)
            {
                return BodyBuildResult.Error(TtsErrorKind.InvalidRule, "POST Form 的 body 必须是查询串或 JSON 对象。");
            }

            return BodyBuildResult.Success(new ParsedTtsRequestBody(
                ParsedTtsRequestBodyKind.FormUrlEncoded,
                bodyElement.Value.ValueKind == JsonValueKind.String
                    ? bodyElement.Value.GetString()
                    : bodyElement.Value.GetRawText(),
                formFields));
        }

        return BodyBuildResult.Success(new ParsedTtsRequestBody(
            ParsedTtsRequestBodyKind.RawText,
            bodyElement.Value.ValueKind == JsonValueKind.String
                ? bodyElement.Value.GetString()
                : bodyElement.Value.GetRawText(),
            null));
    }

    private static IReadOnlyDictionary<string, string>? BuildFormFields(JsonElement bodyElement)
    {
        if (bodyElement.ValueKind == JsonValueKind.Object)
        {
            return bodyElement.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Null => string.Empty,
                        _ => property.Value.GetRawText()
                    },
                    StringComparer.OrdinalIgnoreCase);
        }

        if (bodyElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var pair in (bodyElement.GetString() ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equalsIndex = pair.IndexOf('=', StringComparison.Ordinal);
                if (equalsIndex < 0)
                {
                    fields[Uri.UnescapeDataString(pair)] = string.Empty;
                    continue;
                }

                fields[Uri.UnescapeDataString(pair[..equalsIndex])] =
                    Uri.UnescapeDataString(pair[(equalsIndex + 1)..]);
            }
        }
        catch (UriFormatException)
        {
            return null;
        }

        return fields;
    }

    private static IReadOnlyDictionary<string, string> MergeHeaders(
        IReadOnlyDictionary<string, string> defaults,
        IReadOnlyDictionary<string, string> ruleHeaders)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in defaults)
        {
            merged[pair.Key] = pair.Value;
        }

        foreach (var pair in ruleHeaders)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static string? BuildBodyPreview(ParsedTtsRequestBody body)
    {
        return body.Kind switch
        {
            ParsedTtsRequestBodyKind.None => null,
            ParsedTtsRequestBodyKind.FormUrlEncoded => SensitiveDataRedactor.SerializeRedactedDictionary(body.FormFields),
            _ => SensitiveDataRedactor.RedactJsonLikeText(body.RawText)
        };
    }

    private static string? TryGetHeader(IReadOnlyDictionary<string, string> headers, string key)
    {
        return headers.TryGetValue(key, out var value) ? value : null;
    }

    private static bool IsJsonContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType) &&
               contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFormContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType) &&
               contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record BodyParseResult(bool IsSuccess, JsonElement? BodyElement, string? Message)
    {
        public static BodyParseResult Success(JsonElement? bodyElement) => new(true, bodyElement, null);

        public static BodyParseResult Error(string message) => new(false, null, message);
    }

    private sealed record BodyBuildResult(
        bool IsSuccess,
        ParsedTtsRequestBody? Body,
        TtsErrorKind? Kind,
        string? Message)
    {
        public static BodyBuildResult Success(ParsedTtsRequestBody body) =>
            new(true, body, null, null);

        public static BodyBuildResult Error(TtsErrorKind kind, string message) =>
            new(false, null, kind, message);
    }
}
