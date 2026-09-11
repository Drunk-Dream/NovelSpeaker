using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Infrastructure.Speech;

namespace NovelSpeaker.Infrastructure.Cache;

public sealed class AudioGenerationFailureReporter : IAudioGenerationFailureReporter
{
    private readonly ILogger<AudioGenerationFailureReporter> _logger;

    public AudioGenerationFailureReporter(ILogger<AudioGenerationFailureReporter> logger)
    {
        _logger = logger;
    }

    public void Report(string operation, Exception exception, AudioGenerationRequest request)
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
