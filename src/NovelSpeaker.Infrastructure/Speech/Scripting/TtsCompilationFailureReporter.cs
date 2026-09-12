using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Infrastructure.Diagnostics;

namespace NovelSpeaker.Infrastructure.Speech.Scripting;

public sealed class TtsCompilationFailureReporter : ITtsCompilationFailureReporter
{
    private readonly ILogger<TtsCompilationFailureReporter> _logger;

    public TtsCompilationFailureReporter(ILogger<TtsCompilationFailureReporter> logger)
    {
        _logger = logger;
    }

    public void Report(string operation, Exception exception, IEnumerable<string?> knownSecrets)
    {
        SensitiveFailureLogger.LogError(_logger, LogEventRegistry.TtsCompilationFailed, exception, knownSecrets);
    }
}
