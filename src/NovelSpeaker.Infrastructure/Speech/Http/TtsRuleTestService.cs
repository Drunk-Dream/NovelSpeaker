using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Rules;
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
    private readonly ITtsRuleNormalizer _ruleNormalizer;
    private readonly TimeProvider _timeProvider;
    private readonly IAudioPlayer _audioPlayer;
    private bool _disposed;

    public TtsRuleTestService(
        ITtsRuleLibraryService ruleLibraryService,
        ITtsRequestCompiler requestCompiler,
        IHttpTtsClient httpTtsClient,
        IAudioPlayerFactory audioPlayerFactory,
        ITtsRuleNormalizer? ruleNormalizer = null,
        TimeProvider? timeProvider = null)
    {
        _ruleLibraryService = ruleLibraryService;
        _requestCompiler = requestCompiler;
        _httpTtsClient = httpTtsClient;
        _ruleNormalizer = ruleNormalizer ?? new TtsRuleNormalizer();
        _timeProvider = timeProvider ?? TimeProvider.System;
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
                _ruleNormalizer.Normalize(rule),
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

    private HttpTtsRule BuildRuleFromEditor(TtsRuleEditorModel editor)
    {
        var normalizedEditor = TtsRuleModelMapper.NormalizeEditor(editor);
        return TtsRuleModelMapper.BuildRuleFromEditor(normalizedEditor, existingRule: null, _timeProvider);
    }
}
