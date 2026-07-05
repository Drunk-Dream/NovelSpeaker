using System.Text.Json;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Speech.Rules;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Provides the rules page with试听 capability.
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
        TtsRequestCompilationResult compilation;
        try
        {
            compilation = await _requestCompiler.CompileAsync(
                rule.ToNormalizedRule(),
                new TtsRuleContext(input.SpeakText, input.SpeakSpeed, rule),
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

        if (!compilation.IsSuccess)
        {
            return new TtsRuleTestResult(
                false,
                compilation.Failure?.Message ?? "请求编译失败。",
                null,
                compilation.Warnings,
                compilation.Failure?.Kind,
                compilation.Failure?.StatusCode,
                compilation.Failure?.ResponseContentType,
                compilation.Failure?.ResponseSummary,
                compilation.Failure?.RetryAfter);
        }

        var execution = await _httpTtsClient.ExecuteAsync(compilation.Request!, cancellationToken);
        if (!execution.IsSuccess)
        {
            return new TtsRuleTestResult(
                false,
                execution.Failure?.Message ?? "规则测试失败。",
                null,
                compilation.Warnings,
                execution.Failure?.Kind,
                execution.Failure?.StatusCode,
                execution.Failure?.ResponseContentType,
                execution.Failure?.ResponseSummary,
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
                null,
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
            null,
            compilation.Warnings,
            null,
            execution.Audio!.StatusCode,
            execution.Audio.ResponseContentType,
            execution.Audio.DetectedAudioFormat,
            null);
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
            editor.LastUpdateTime,
            string.Empty,
            editor.IsEnabled,
            editor.CompatibilityStatus,
            editor.UnsupportedFields,
            null,
            utcNow,
            utcNow);

        return rule with { RuleJson = NovelSpeakerRuleJsonSerializer.Serialize(rule) };
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
        var hasBody = !string.IsNullOrWhiteSpace(requestOptions.Body);
        if (!hasMethod && !hasBody)
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

        if (hasBody)
        {
            writer.WritePropertyName("body");
            WriteJsonLikeValue(writer, requestOptions.Body!);
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
}
