using NovelSpeaker.Domain.Speech.Providers;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Infrastructure.Diagnostics;
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
        string?[] providerSecrets = request.Provider.Provider.Configuration is HttpSpeechProviderConfiguration http
            ? [http.UrlTemplate, http.BodyTemplate, .. http.Headers.SelectMany(static pair => new[] { pair.Key, pair.Value })]
            : [];
        SensitiveFailureLogger.LogError(
            _logger,
            LogEventRegistry.CacheOperationFailed,
            exception,
            [
                request.SpeechText,
                .. providerSecrets
            ]);
    }
}
