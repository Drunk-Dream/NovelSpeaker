namespace NovelSpeaker.Application.Speech.Execution;

public interface ITtsRetryPolicy
{
    bool ShouldRetry(int completedRetries, TtsTransportFailureKind? transportFailure, int? statusCode);
}
