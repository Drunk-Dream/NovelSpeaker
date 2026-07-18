using NovelSpeaker.Application.Speech.Execution;

namespace NovelSpeaker.Infrastructure.Speech.Http;

public sealed class TtsRetryPolicy : ITtsRetryPolicy
{
    private const int MaxRetries = 2;

    public bool ShouldRetry(int completedRetries, TtsTransportFailureKind? transportFailure, int? statusCode) =>
        completedRetries < MaxRetries &&
        (transportFailure is TtsTransportFailureKind.Timeout or TtsTransportFailureKind.Network || statusCode >= 500);
}
