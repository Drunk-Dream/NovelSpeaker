using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Compiles the selected rule, executes HTTP TTS, and returns a local audio file for playback.
/// </summary>
public sealed class PlaybackAudioProvider : IPlaybackAudioProvider
{
    private static readonly IReadOnlyDictionary<string, string> EmptyLoginInfo =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly ITtsRequestCompiler _requestCompiler;
    private readonly IHttpTtsClient _httpTtsClient;
    private readonly IAudioCache _audioCache;

    public PlaybackAudioProvider(
        ITtsRequestCompiler requestCompiler,
        IHttpTtsClient httpTtsClient,
        IAudioCache audioCache)
    {
        _requestCompiler = requestCompiler;
        _httpTtsClient = httpTtsClient;
        _audioCache = audioCache;
    }

    public async Task<PlaybackAudioResult> GetAudioAsync(PlaybackAudioRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = CreateCacheKey(request);
        var cached = await _audioCache.TryGetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return new PlaybackAudioResult(cached.FilePath, true, null);
        }

        TtsRequestCompilationResult compilation;
        try
        {
            compilation = await _requestCompiler.CompileAsync(
                request.NormalizedRule,
                new TtsRuleContext(
                    request.SpeechText,
                    request.SpeakSpeed,
                    request.SourceRule,
                    EmptyLoginInfo),
                cancellationToken);
        }
        catch (FormatException exception)
        {
            return new PlaybackAudioResult(
                null,
                false,
                new TtsExecutionFailure(TtsErrorKind.InvalidRule, $"规则模板格式无效：{exception.Message}", null, null, null, null));
        }

        if (!compilation.IsSuccess)
        {
            return new PlaybackAudioResult(null, false, compilation.Failure);
        }

        var execution = await _httpTtsClient.ExecuteAsync(compilation.Request!, cancellationToken);
        if (!execution.IsSuccess)
        {
            return new PlaybackAudioResult(null, false, execution.Failure);
        }

        var stored = await _audioCache.StoreAsync(cacheKey, execution.Audio!.FilePath, cancellationToken);
        return new PlaybackAudioResult(stored.FilePath, false, null);
    }

    public Task InvalidateAsync(PlaybackAudioRequest request, CancellationToken cancellationToken)
    {
        return _audioCache.InvalidateAsync(CreateCacheKey(request), cancellationToken);
    }

    private static AudioCacheKey CreateCacheKey(PlaybackAudioRequest request)
    {
        return AudioCacheKey.FromPlayback(
            request.BookId,
            request.ChapterIndex,
            request.SegmentIndex,
            request.RuleId,
            request.SpeakSpeed,
            request.SpeechText);
    }
}
