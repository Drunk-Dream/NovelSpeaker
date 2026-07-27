using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Testing;

/// <summary>
/// Provides the rules page with试听 capability.
/// </summary>
public sealed class TtsRuleTestService : ITtsRuleTestService, IAsyncDisposable
{
    private readonly ITtsRuleEditorUseCase _ruleEditor;
    private readonly ITtsRequestCompiler _requestCompiler;
    private readonly IHttpTtsClient _httpTtsClient;
    private readonly ITtsRuleNormalizer _ruleNormalizer;
    private readonly IAudioPlayer _audioPlayer;
    private readonly ITtsRuleTestFailureReporter? _failureReporter;
    private TtsAudioResponse? _currentAudio;
    private bool _disposed;

    public TtsRuleTestService(
        ITtsRuleEditorUseCase ruleEditor,
        ITtsRequestCompiler requestCompiler,
        IHttpTtsClient httpTtsClient,
        IAudioPlayerFactory audioPlayerFactory,
        ITtsRuleNormalizer? ruleNormalizer = null,
        ITtsRuleTestFailureReporter? failureReporter = null)
    {
        _ruleEditor = ruleEditor;
        _requestCompiler = requestCompiler;
        _httpTtsClient = httpTtsClient;
        _ruleNormalizer = ruleNormalizer ?? new TtsRuleNormalizer();
        _audioPlayer = audioPlayerFactory.Create();
        _failureReporter = failureReporter;
    }

    public async Task<TtsRuleTestResult> TestAsync(
        TtsRuleDraftTestInput input,
        CancellationToken cancellationToken)
    {
        var preparation = await _ruleEditor.PrepareDraftAsync(input.Editor, cancellationToken);
        if (!preparation.IsValid)
        {
            return new TtsRuleTestResult(
                false,
                string.Join(" ", preparation.Validation.Errors),
                null,
                Array.Empty<string>(),
                TtsErrorKind.InvalidRule,
                null,
                null,
                null,
                null);
        }

        var rule = preparation.CandidateRule!;
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
            LogFailure(input, exception, "TTS rule test normalization");
            return new TtsRuleTestResult(
                false,
                "规则模板格式无效，请检查规则后重试。",
                null,
                Array.Empty<string>(),
                TtsErrorKind.InvalidRule,
                null,
                null,
                null,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(input, exception, "TTS rule test compilation");
            return CreateUnexpectedFailureResult();
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

        TtsHttpExecutionResult execution;
        try
        {
            execution = await _httpTtsClient.ExecuteAsync(compilation.Request!, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(input, exception, "TTS rule test HTTP execution");
            return CreateUnexpectedFailureResult();
        }
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

        var downloadedAudio = execution.Audio!;
        var ownsDownloadedAudio = true;
        try
        {
            _audioPlayer.Stop();
            await _audioPlayer.LoadAsync(downloadedAudio.FilePath, cancellationToken);
            _audioPlayer.Play();

            var previousAudio = _currentAudio;
            _currentAudio = downloadedAudio;
            ownsDownloadedAudio = false;
            if (previousAudio is not null)
            {
                await previousAudio.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var playbackError = PlaybackErrorMapper.Map(exception);
            LogFailure(input, exception, "TTS rule test audio playback");
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
        finally
        {
            if (ownsDownloadedAudio)
            {
                await downloadedAudio.DisposeAsync().ConfigureAwait(false);
            }
        }

        return new TtsRuleTestResult(
            true,
            "已获取并开始播放试听音频。",
            null,
            compilation.Warnings,
            null,
            downloadedAudio.StatusCode,
            downloadedAudio.ResponseContentType,
            downloadedAudio.DetectedAudioFormat,
            null);
    }

    private void LogFailure(TtsRuleDraftTestInput input, Exception exception, string operation)
    {
        _failureReporter?.Report(operation, exception, input);
    }

    private static TtsRuleTestResult CreateUnexpectedFailureResult()
    {
        return new TtsRuleTestResult(
            false,
            "规则测试失败，请稍后重试。",
            null,
            [],
            TtsErrorKind.Unknown,
            null,
            null,
            null,
            null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _audioPlayer.DisposeAsync();
        }
        finally
        {
            var currentAudio = Interlocked.Exchange(ref _currentAudio, null);
            if (currentAudio is not null)
            {
                await currentAudio.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

}
