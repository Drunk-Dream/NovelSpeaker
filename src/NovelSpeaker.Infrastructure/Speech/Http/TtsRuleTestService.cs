using System.Text.Json;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Speech.Rules;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Provides the rules page with request preview,试听, and cookie-reset capabilities.
/// </summary>
public sealed class TtsRuleTestService : ITtsRuleTestService, IAsyncDisposable
{
    private readonly ITtsRuleLibraryService _ruleLibraryService;
    private readonly ITtsRequestCompiler _requestCompiler;
    private readonly IHttpTtsClient _httpTtsClient;
    private readonly IAudioPlayer _audioPlayer;
    private bool _disposed;

    public TtsRuleTestService(
        ITtsRuleLibraryService ruleLibraryService,
        ITtsRequestCompiler requestCompiler,
        IHttpTtsClient httpTtsClient,
        IAudioPlayerFactory audioPlayerFactory)
    {
        _ruleLibraryService = ruleLibraryService;
        _requestCompiler = requestCompiler;
        _httpTtsClient = httpTtsClient;
        _audioPlayer = audioPlayerFactory.Create();
    }

    public async Task<TtsRuleTestPreviewResult> CreatePreviewAsync(
        TtsRuleDraftTestInput input,
        CancellationToken cancellationToken)
    {
        var validation = await _ruleLibraryService.ValidateEditorAsync(input.Editor, cancellationToken);
        if (!validation.IsValid)
        {
            return new TtsRuleTestPreviewResult(
                false,
                string.Join(" ", validation.Errors),
                null,
                Array.Empty<string>(),
                TtsErrorKind.InvalidRule);
        }

        var rule = BuildRuleFromEditor(validation.NormalizedModel);
        var loginInfo = ParseLoginInfo(validation.NormalizedModel.LoginInfo);
        TtsRequestCompilationResult compilation;
        try
        {
            compilation = await _requestCompiler.CompileAsync(
                rule.ToNormalizedRule(),
                new TtsRuleContext(input.SpeakText, input.SpeakSpeed, rule, loginInfo),
                cancellationToken);
        }
        catch (FormatException exception)
        {
            return new TtsRuleTestPreviewResult(
                false,
                $"规则模板格式无效：{exception.Message}",
                null,
                Array.Empty<string>(),
                TtsErrorKind.InvalidRule);
        }

        var preview = RedactPreview(compilation.Preview, loginInfo);
        return compilation.IsSuccess
            ? new TtsRuleTestPreviewResult(true, "已生成请求预览。", preview, compilation.Warnings, null)
            : new TtsRuleTestPreviewResult(
                false,
                RedactText(compilation.Failure?.Message ?? "生成请求预览失败。", loginInfo) ?? "生成请求预览失败。",
                preview,
                compilation.Warnings,
                compilation.Failure?.Kind);
    }

    public async Task<TtsRuleTestResult> TestAsync(
        TtsRuleDraftTestInput input,
        CancellationToken cancellationToken)
    {
        var validation = await _ruleLibraryService.ValidateEditorAsync(input.Editor, cancellationToken);
        if (!validation.IsValid)
        {
            return new TtsRuleTestResult(
                false,
                string.Join(" ", validation.Errors),
                null,
                Array.Empty<string>(),
                TtsErrorKind.InvalidRule,
                null,
                null,
                null,
                null);
        }

        var rule = BuildRuleFromEditor(validation.NormalizedModel);
        var loginInfo = ParseLoginInfo(validation.NormalizedModel.LoginInfo);
        TtsRequestCompilationResult compilation;
        try
        {
            compilation = await _requestCompiler.CompileAsync(
                rule.ToNormalizedRule(),
                new TtsRuleContext(input.SpeakText, input.SpeakSpeed, rule, loginInfo),
                cancellationToken);
        }
        catch (FormatException exception)
        {
            return new TtsRuleTestResult(
                false,
                $"规则模板格式无效：{exception.Message}",
                null,
                Array.Empty<string>(),
                TtsErrorKind.InvalidRule,
                null,
                null,
                null,
                null);
        }

        var preview = RedactPreview(compilation.Preview, loginInfo);
        if (!compilation.IsSuccess)
        {
            return new TtsRuleTestResult(
                false,
                RedactText(compilation.Failure?.Message ?? "请求编译失败。", loginInfo) ?? "请求编译失败。",
                preview,
                compilation.Warnings,
                compilation.Failure?.Kind,
                compilation.Failure?.StatusCode,
                compilation.Failure?.ResponseContentType,
                RedactText(compilation.Failure?.ResponseSummary, loginInfo),
                compilation.Failure?.RetryAfter);
        }

        var execution = await _httpTtsClient.ExecuteAsync(compilation.Request!, cancellationToken);
        if (!execution.IsSuccess)
        {
            return new TtsRuleTestResult(
                false,
                RedactText(execution.Failure?.Message ?? "规则测试失败。", loginInfo) ?? "规则测试失败。",
                preview,
                compilation.Warnings,
                execution.Failure?.Kind,
                execution.Failure?.StatusCode,
                execution.Failure?.ResponseContentType,
                RedactText(execution.Failure?.ResponseSummary, loginInfo),
                execution.Failure?.RetryAfter);
        }

        try
        {
            _audioPlayer.Stop();
            await _audioPlayer.LoadAsync(execution.Audio!.FilePath, cancellationToken);
            _audioPlayer.Play();
        }
        catch (Exception exception)
        {
            var playbackError = PlaybackErrorMapper.Map(exception);
            return new TtsRuleTestResult(
                false,
                $"音频已下载，但本地播放失败：{playbackError.Message}",
                preview,
                compilation.Warnings,
                TtsErrorKind.AudioDecode,
                execution.Audio!.StatusCode,
                execution.Audio.ResponseContentType,
                null,
                null);
        }

        return new TtsRuleTestResult(
            true,
            "已获取并开始播放试听音频。",
            preview,
            compilation.Warnings,
            null,
            execution.Audio!.StatusCode,
            execution.Audio.ResponseContentType,
            RedactText(execution.Audio.DetectedAudioFormat, loginInfo),
            null);
    }

    public Task ClearRuleCookiesAsync(long ruleId, CancellationToken cancellationToken)
    {
        return _httpTtsClient.ClearRuleCookiesAsync(ruleId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _audioPlayer.DisposeAsync();
    }

    private static HttpTtsRule BuildRuleFromEditor(TtsRuleEditorModel editor)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        var rule = new HttpTtsRule(
            editor.Id ?? 0,
            editor.Name,
            editor.Url,
            editor.ContentType,
            editor.ConcurrentRate,
            SerializeKeyValueJson(editor.Headers),
            SerializeRequestOptions(editor.RequestOptions),
            editor.EnabledCookieJar,
            editor.LastUpdateTime,
            string.Empty,
            editor.IsEnabled,
            editor.CompatibilityStatus,
            editor.UnsupportedFields,
            null,
            utcNow,
            utcNow)
        {
            LoginInfoJson = SerializeKeyValueJson(editor.LoginInfo)
        };

        return rule with { RuleJson = NovelSpeakerRuleJsonSerializer.Serialize(rule) };
    }

    private static IReadOnlyDictionary<string, string> ParseLoginInfo(IReadOnlyList<TtsRuleEditorKeyValue> entries)
    {
        return entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? SerializeKeyValueJson(IReadOnlyList<TtsRuleEditorKeyValue> entries)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var dictionary = entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(dictionary);
    }

    private static string? SerializeRequestOptions(TtsRuleRequestOptionsEditor requestOptions)
    {
        var hasMethod = !string.IsNullOrWhiteSpace(requestOptions.Method);
        var hasHeaders = requestOptions.Headers.Count > 0;
        var hasBody = !string.IsNullOrWhiteSpace(requestOptions.Body);
        var hasTimeout = requestOptions.TimeoutMs is not null;
        if (!hasMethod && !hasHeaders && !hasBody && !hasTimeout)
        {
            return null;
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();

        if (hasMethod)
        {
            writer.WriteString("method", requestOptions.Method);
        }

        if (hasHeaders)
        {
            writer.WritePropertyName("headers");
            writer.WriteStartObject();
            foreach (var header in requestOptions.Headers)
            {
                writer.WriteString(header.Key, header.Value);
            }

            writer.WriteEndObject();
        }

        if (hasBody)
        {
            writer.WritePropertyName("body");
            WriteJsonLikeValue(writer, requestOptions.Body!);
        }

        if (hasTimeout)
        {
            writer.WriteNumber("timeoutMs", requestOptions.TimeoutMs!.Value);
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteJsonLikeValue(Utf8JsonWriter writer, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteStringValue(text);
        }
    }

    private static TtsRequestPreview? RedactPreview(
        TtsRequestPreview? preview,
        IReadOnlyDictionary<string, string> loginInfo)
    {
        if (preview is null)
        {
            return null;
        }

        return preview with
        {
            Url = RedactText(preview.Url, loginInfo) ?? string.Empty,
            HeadersJson = RedactText(preview.HeadersJson, loginInfo),
            BodyPreview = RedactText(preview.BodyPreview, loginInfo),
            DeclaredContentType = RedactText(preview.DeclaredContentType, loginInfo)
        };
    }

    private static string? RedactText(string? text, IReadOnlyDictionary<string, string> loginInfo)
    {
        return SensitiveDataRedactor.RedactKnownSecrets(text, loginInfo.Values);
    }
}
