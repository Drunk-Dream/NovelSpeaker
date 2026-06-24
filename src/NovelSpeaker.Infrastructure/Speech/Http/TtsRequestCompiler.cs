using System.Text.Json;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Evaluates rule templates and normalizes the result into a concrete HTTP request shape.
/// </summary>
public sealed class TtsRequestCompiler : ITtsRequestCompiler
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
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

        var headers = MergeHeaders(DefaultHeaders, ruleHeadersResult.Headers!, requestOptionsResult.Headers!);
        var bodyResult = BuildBody(requestOptionsResult.BodyElement, headers);
        if (!bodyResult.IsSuccess)
        {
            return Failure(bodyResult.Kind!.Value, bodyResult.Message!);
        }

        var timeout = requestOptionsResult.Timeout ?? DefaultTimeout;
        var request = new ParsedTtsRequest(
            rule.RuleId,
            method,
            url,
            headers,
            bodyResult.Body!,
            rule.DeclaredContentType,
            timeout);

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
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
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
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            JsonElement? body = null;
            TimeSpan? timeout = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "method":
                        method = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.GetRawText();
                        break;
                    case "headers":
                        {
                            var headerResult = ParseHeadersElement(property.Value, "requestOptions.headers");
                            if (!headerResult.IsSuccess)
                            {
                                return RequestOptionsParseResult.Error(headerResult.Kind!.Value, headerResult.Message!);
                            }

                            headers = new Dictionary<string, string>(headerResult.Headers!, StringComparer.OrdinalIgnoreCase);
                            break;
                        }
                    case "body":
                        body = property.Value.Clone();
                        break;
                    case "timeoutMs":
                        {
                            var timeoutResult = ParseTimeout(property.Value);
                            if (!timeoutResult.IsSuccess)
                            {
                                return RequestOptionsParseResult.Error(timeoutResult.Kind!.Value, timeoutResult.Message!);
                            }

                            timeout = timeoutResult.Timeout;
                            break;
                        }
                    default:
                        return RequestOptionsParseResult.Error(
                            TtsErrorKind.InvalidRule,
                            $"requestOptions 包含当前版本不支持的字段：{property.Name}");
                }
            }

            return RequestOptionsParseResult.Success(method, body, headers, timeout);
        }
        catch (JsonException exception)
        {
            return RequestOptionsParseResult.Error(
                TtsErrorKind.InvalidRule,
                $"requestOptions 不是有效的 JSON 对象：{exception.Message}");
        }
    }

    private static HeaderParseResult ParseHeadersElement(JsonElement value, string fieldName)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => ParseHeaderDictionary(value.GetRawText(), fieldName),
            JsonValueKind.String => ParseHeaderDictionary(value.GetString(), fieldName),
            _ => HeaderParseResult.Error(TtsErrorKind.InvalidRule, $"字段 {fieldName} 必须是 JSON 对象。")
        };
    }

    private static TimeoutParseResult ParseTimeout(JsonElement value)
    {
        int? timeoutMilliseconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };

        if (timeoutMilliseconds is null || timeoutMilliseconds <= 0)
        {
            return TimeoutParseResult.Error(TtsErrorKind.InvalidRule, "timeoutMs 必须是正整数毫秒值。");
        }

        if (timeoutMilliseconds > 300000)
        {
            return TimeoutParseResult.Error(TtsErrorKind.InvalidRule, "timeoutMs 不能超过 300000 毫秒。");
        }

        return TimeoutParseResult.Success(TimeSpan.FromMilliseconds(timeoutMilliseconds.Value));
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
        IReadOnlyDictionary<string, string> ruleHeaders,
        IReadOnlyDictionary<string, string> requestOptionHeaders)
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

        foreach (var pair in requestOptionHeaders)
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
        IReadOnlyDictionary<string, string>? Headers,
        TimeSpan? Timeout,
        TtsErrorKind? Kind,
        string? Message)
    {
        public static RequestOptionsParseResult Success(
            string? method,
            JsonElement? bodyElement,
            IReadOnlyDictionary<string, string> headers,
            TimeSpan? timeout) =>
            new(true, method, bodyElement, headers, timeout, null, null);

        public static RequestOptionsParseResult Error(TtsErrorKind kind, string message) =>
            new(false, null, null, null, null, kind, message);
    }

    private sealed record TimeoutParseResult(
        bool IsSuccess,
        TimeSpan? Timeout,
        TtsErrorKind? Kind,
        string? Message)
    {
        public static TimeoutParseResult Success(TimeSpan timeout) =>
            new(true, timeout, null, null);

        public static TimeoutParseResult Error(TtsErrorKind kind, string message) =>
            new(false, null, kind, message);
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
