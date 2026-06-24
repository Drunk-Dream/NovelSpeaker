using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Playback;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Provides the rules page with request preview,试听, and cookie-reset capabilities.
/// </summary>
public sealed class TtsRuleTestService : ITtsRuleTestService, IAsyncDisposable
{
    private readonly ITtsRuleRepository _repository;
    private readonly ITtsRequestCompiler _requestCompiler;
    private readonly IHttpTtsClient _httpTtsClient;
    private readonly IAudioPlayer _audioPlayer;
    private bool _disposed;

    public TtsRuleTestService(
        ITtsRuleRepository repository,
        ITtsRequestCompiler requestCompiler,
        IHttpTtsClient httpTtsClient,
        IAudioPlayerFactory audioPlayerFactory)
    {
        _repository = repository;
        _requestCompiler = requestCompiler;
        _httpTtsClient = httpTtsClient;
        _audioPlayer = audioPlayerFactory.Create();
    }

    public async Task<TtsRuleTestPreviewResult> CreatePreviewAsync(
        TtsRuleTestInput input,
        CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(input.RuleId, cancellationToken);
        if (rule is null)
        {
            return new TtsRuleTestPreviewResult(false, "未找到要测试的规则。", null, Array.Empty<string>(), TtsErrorKind.InvalidRule);
        }

        TtsRequestCompilationResult compilation;
        try
        {
            compilation = await _requestCompiler.CompileAsync(
                rule.ToNormalizedRule(),
                new TtsRuleContext(input.SpeakText, input.SpeakSpeed, rule, input.LoginInfo),
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

        return compilation.IsSuccess
            ? new TtsRuleTestPreviewResult(true, "已生成请求预览。", compilation.Preview, compilation.Warnings, null)
            : new TtsRuleTestPreviewResult(
                false,
                compilation.Failure?.Message ?? "生成请求预览失败。",
                compilation.Preview,
                compilation.Warnings,
                compilation.Failure?.Kind);
    }

    public async Task<TtsRuleTestResult> TestAsync(
        TtsRuleTestInput input,
        CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(input.RuleId, cancellationToken);
        if (rule is null)
        {
            return new TtsRuleTestResult(
                false,
                "未找到要测试的规则。",
                null,
                Array.Empty<string>(),
                TtsErrorKind.InvalidRule,
                null,
                null,
                null,
                null);
        }

        TtsRequestCompilationResult compilation;
        try
        {
            compilation = await _requestCompiler.CompileAsync(
                rule.ToNormalizedRule(),
                new TtsRuleContext(input.SpeakText, input.SpeakSpeed, rule, input.LoginInfo),
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
                compilation.Preview,
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
                compilation.Preview,
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
                compilation.Preview,
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
            compilation.Preview,
            compilation.Warnings,
            null,
            execution.Audio!.StatusCode,
            execution.Audio.ResponseContentType,
            execution.Audio.DetectedAudioFormat,
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
}
