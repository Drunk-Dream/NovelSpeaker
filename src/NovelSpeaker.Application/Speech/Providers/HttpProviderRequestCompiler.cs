using System.Text.Json;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Security;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Compiles an HTTP Provider's templates into a validated transport request.</summary>
public sealed class HttpProviderRequestCompiler(ITemplateEvaluator evaluator)
{
    private static readonly IReadOnlyDictionary<string, string> DefaultHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "*/*",
            ["User-Agent"] = "NovelSpeaker/1.0"
        };

    public async Task<TtsRequestCompilationResult> CompileAsync(
        HttpSpeechProviderConfiguration configuration,
        ProviderSynthesisRequest synthesis,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new SpeechTemplateContext(synthesis.Text, synthesis.SpeakSpeed);
        string urlText;
        string? bodyText;
        var headers = new Dictionary<string, string>(DefaultHeaders, StringComparer.OrdinalIgnoreCase);
        try
        {
            urlText = await evaluator.EvaluateAsync(
                NormalizedTemplate.Parse(configuration.UrlTemplate), context, cancellationToken).ConfigureAwait(false);
            foreach (var header in configuration.Headers)
            {
                var value = await evaluator.EvaluateAsync(
                    NormalizedTemplate.Parse(header.Value), context, cancellationToken).ConfigureAwait(false);
                if (value.Any(character => character is '\r' or '\n' or '\0'))
                {
                    return Failure(TtsErrorKind.InvalidRule, "HTTP Provider Header 值无效。");
                }

                headers[header.Key] = value;
            }

            bodyText = configuration.BodyTemplate is null
                ? null
                : await evaluator.EvaluateAsync(
                    NormalizedTemplate.Parse(configuration.BodyTemplate), context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(TtsErrorKind.ScriptError, "HTTP Provider 模板求值失败。");
        }

        if (!HttpProviderUriValidator.TryCreateAbsoluteHttpUri(urlText, out var url))
        {
            return Failure(TtsErrorKind.InvalidRule, "HTTP Provider URL 无效。");
        }

        if (configuration.Method == "GET" && configuration.BodyTemplate is not null)
        {
            return Failure(TtsErrorKind.InvalidRule, "GET 请求不能携带 Body。");
        }

        if (headers.TryGetValue("Content-Type", out var contentType) &&
            !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out _))
        {
            return Failure(TtsErrorKind.InvalidRule, "HTTP Provider Content-Type 无效。");
        }

        var body = BuildBody(bodyText, headers);
        if (body is null)
        {
            return Failure(TtsErrorKind.InvalidRule, "HTTP Provider 请求 Body 与 Content-Type 不匹配。");
        }

        var parsed = new ParsedTtsRequest(0, configuration.Method, url!, headers, body, null);
        var preview = new TtsRequestPreview(
            parsed.Method,
            SensitiveDataRedactor.RedactUrl(parsed.Url.ToString()),
            SensitiveDataRedactor.SerializeRedactedDictionary(parsed.Headers),
            body.Kind == ParsedTtsRequestBodyKind.FormUrlEncoded
                ? SensitiveDataRedactor.SerializeRedactedDictionary(body.FormFields)
                : SensitiveDataRedactor.RedactJsonLikeText(body.RawText),
            null);
        return new TtsRequestCompilationResult(parsed, preview, [], null);
    }

    private static ParsedTtsRequestBody? BuildBody(
        string? text,
        IReadOnlyDictionary<string, string> headers)
    {
        if (text is null)
        {
            return ParsedTtsRequestBody.None;
        }

        headers.TryGetValue("Content-Type", out var contentType);
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var _ = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                return null;
            }

            return new ParsedTtsRequestBody(ParsedTtsRequestBodyKind.Json, text, null);
        }

        if (string.Equals(mediaType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var component in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = component.IndexOf('=');
                try
                {
                    var key = Uri.UnescapeDataString((separator < 0 ? component : component[..separator]).Replace('+', ' '));
                    var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(component[(separator + 1)..].Replace('+', ' '));
                    fields[key] = value;
                }
                catch (UriFormatException)
                {
                    return null;
                }
            }

            return new ParsedTtsRequestBody(ParsedTtsRequestBodyKind.FormUrlEncoded, text, fields);
        }

        return new ParsedTtsRequestBody(ParsedTtsRequestBodyKind.RawText, text, null);
    }

    private static TtsRequestCompilationResult Failure(TtsErrorKind kind, string message) =>
        new(null, null, [], new TtsExecutionFailure(kind, message, null, null, null, null));
}
