using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Infrastructure.Speech;

namespace NovelSpeaker.Infrastructure.Playback;

public sealed class PlaybackAudioFailureReporter : IPlaybackAudioFailureReporter
{
    private readonly ILogger<PlaybackAudioFailureReporter> _logger;

    public PlaybackAudioFailureReporter(ILogger<PlaybackAudioFailureReporter> logger)
    {
        _logger = logger;
    }

    public void Report(string operation, Exception exception, PlaybackAudioRequest request)
    {
        SensitiveFailureLogger.LogError(
            _logger,
            operation,
            exception,
            [
                request.SpeechText,
                request.SourceRule.Url,
                request.SourceRule.RequestBody,
                .. request.SourceRule.Headers.SelectMany(static pair => new[] { pair.Key, pair.Value })
            ]);
    }
}
