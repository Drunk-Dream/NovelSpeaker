namespace NovelSpeaker.Application.Speech.Providers;

public sealed record ProviderSynthesisResult(
    Stream? Audio,
    string? ContentType,
    ProviderSynthesisFailure? Failure,
    string? AudioFormat = null)
{
    public bool IsSuccess => Audio is not null && Failure is null;
}
