using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Testing;

namespace NovelSpeaker.Infrastructure.Speech.Http;

public sealed class TtsRuleTestFailureReporter : ITtsRuleTestFailureReporter
{
    private readonly ILogger<TtsRuleTestFailureReporter> _logger;

    public TtsRuleTestFailureReporter(ILogger<TtsRuleTestFailureReporter> logger)
    {
        _logger = logger;
    }

    public void Report(string operation, Exception exception, TtsRuleDraftTestInput input)
    {
        SensitiveFailureLogger.LogError(
            _logger,
            operation,
            exception,
            [
                input.SpeakText,
                input.Editor.Url,
                input.Editor.RequestOptions.Body,
                .. input.Editor.Headers.SelectMany(static pair => new[] { pair.Key, pair.Value })
            ]);
    }
}
