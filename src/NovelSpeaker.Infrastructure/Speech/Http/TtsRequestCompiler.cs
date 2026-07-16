using System.Text.Json;
using NovelSpeaker.Application.Speech;
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

    public TtsRequestCompiler(ITemplateEvaluator templateEvaluator)
    {
        _templateEvaluator = templateEvaluator;
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
        string? headerText;
        string? requestOptionsText;
        try
        {
            urlText = await _templateEvaluator.EvaluateAsync(rule.UrlTemplate, context, cancellationToken);
            headerText = rule.HeaderTemplate is null
                ? null
                : await _templateEvaluator.EvaluateAsync(rule.HeaderTemplate, context, cancellationToken);
            requestOptionsText = rule.RequestOptionsTemplate is null
                ? null
                : await _templateEvaluator.EvaluateAsync(rule.RequestOptionsTemplate, context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Failure(TtsErrorKind.Cancelled, "已取消当前请求编译。");
        }
        catch (Exception exception)
        {
            return Failure(TtsErrorKind.ScriptError, $"模板求值失败：{exception.Message}");
        }

        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url))
        {
            return Failure(TtsErrorKind.InvalidRule, "规则 URL 不是有效的绝对地址。");
        }

        var ruleHeadersResult = ParseHeaderDictionary(headerText, "header");
        if (!ruleHeadersResult.IsSuccess)
        {
            return Failure(ruleHeadersResult.Kind!.Value, ruleHeadersResult.Message!);
        }

        var requestOptionsResult = ParseRequestOptions(requestOptionsText);
        if (!requestOptionsResult.IsSuccess)
        {
            return Failure(requestOptionsResult.Kind!.Value, requestOptionsResult.Message!);
        }

        var method = string.IsNullOrWhiteSpace(requestOptionsResult.Method)
            ? requestOptionsResult.BodyElement is null ? "GET" : "POST"
            : requestOptionsResult.Method!.Trim().ToUpperInvariant();
        if (method is not ("GET" or "POST"))
        {
            return Failure(TtsErrorKind.InvalidRule, $"当前版本仅支持 GET 和 POST，请求方法 {method} 不受支持。");
        }

        if (method == "GET" && requestOptionsResult.BodyElement is not null)
        {
            return Failure(TtsErrorKind.InvalidRule, "GET 请求不能携带 body。");
        }

        var headers = MergeHeaders(DefaultHeaders, ruleHeadersResult.Headers!);
        if (TtsRuleCompatibilityChecker.ContainsCookieHeader(headers))
        {
            return Failure(
                TtsErrorKind.InvalidRule,
                TtsRuleCompatibilityChecker.UnsupportedCookieLoginInfoMessage);
        }

        var bodyResult = BuildBody(requestOptionsResult.BodyElement, headers);
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

    private static TtsRequestCompilationResult Failure(TtsErrorKind kind, string message)
    {
        return new TtsRequestCompilationResult(
            null,
            null,
            Array.Empty<string>(),
            new TtsExecutionFailure(kind, message, null, null, null, null));
    }

    private static HeaderParseResult ParseHeaderDictionary(string? headerText, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(headerText))
        {
            return HeaderParseResult.Success(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            using var document = JsonDocument.Parse(headerText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return HeaderParseResult.Error(TtsErrorKind.InvalidRule, $"字段 {fieldName} 必须是 JSON 对象。");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                headers[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Null => string.Empty,
                    _ => property.Value.GetRawText()
                };
            }

            return HeaderParseResult.Success(headers);
        }
        catch (JsonException exception)
        {
            return HeaderParseResult.Error(
                TtsErrorKind.InvalidRule,
                $"字段 {fieldName} 不是有效的 JSON 对象：{exception.Message}");
        }
    }

    private static RequestOptionsParseResult ParseRequestOptions(string? requestOptionsText)
    {
        if (string.IsNullOrWhiteSpace(requestOptionsText))
        {
            return RequestOptionsParseResult.Success(
                null,
                null);
        }

        try
        {
            using var document = JsonDocument.Parse(requestOptionsText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return RequestOptionsParseResult.Error(TtsErrorKind.InvalidRule, "requestOptions 必须是 JSON 对象。");
            }

            string? method = null;
            JsonElement? body = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "method":
                        method = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.GetRawText();
                        break;
                    case "body":
                        body = property.Value.Clone();
                        break;
                    case "timeoutMs":
                        break;
                    default:
                        return RequestOptionsParseResult.Error(
                            TtsErrorKind.InvalidRule,
                            $"requestOptions 包含当前版本不支持的字段：{property.Name}");
                }
            }

            return RequestOptionsParseResult.Success(method, body);
        }
        catch (JsonException exception)
        {
            return RequestOptionsParseResult.Error(
                TtsErrorKind.InvalidRule,
                $"requestOptions 不是有效的 JSON 对象：{exception.Message}");
        }
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

    private sealed record HeaderParseResult(
        bool IsSuccess,
        IReadOnlyDictionary<string, string>? Headers,
        TtsErrorKind? Kind,
        string? Message)
    {
        public static HeaderParseResult Success(IReadOnlyDictionary<string, string> headers) =>
            new(true, headers, null, null);

        public static HeaderParseResult Error(TtsErrorKind kind, string message) =>
            new(false, null, kind, message);
    }

    private sealed record RequestOptionsParseResult(
        bool IsSuccess,
        string? Method,
        JsonElement? BodyElement,
        TtsErrorKind? Kind,
        string? Message)
    {
        public static RequestOptionsParseResult Success(
            string? method,
            JsonElement? bodyElement) =>
            new(true, method, bodyElement, null, null);

        public static RequestOptionsParseResult Error(TtsErrorKind kind, string message) =>
            new(false, null, null, kind, message);
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
