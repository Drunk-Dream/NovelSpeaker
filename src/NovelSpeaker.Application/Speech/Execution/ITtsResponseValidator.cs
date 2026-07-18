using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Application.Speech.Execution;

public interface ITtsResponseValidator
{
    Task<TtsHttpExecutionResult> ValidateAsync(
        ParsedTtsRequest request,
        TtsTransportResponse response,
        CancellationToken cancellationToken);
}
