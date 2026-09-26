using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public interface IProviderRuntime
{
    SpeechProviderType Type { get; }

    Task<ProviderSynthesisResult> SynthesizeAsync(
        SpeechProviderInstance provider,
        ProviderSynthesisRequest request,
        CancellationToken cancellationToken);
}

public interface IProviderRuntimeResolver
{
    Task<ProviderRuntimeResolution> ResolveAsync(
        ProviderId? providerId,
        CancellationToken cancellationToken);
}

public sealed record ProviderSynthesisRequest(string Text, int SpeakSpeed);

public sealed record ProviderSynthesisResult(
    Stream? Audio,
    string? ContentType,
    ProviderSynthesisFailure? Failure)
{
    public bool IsSuccess => Audio is not null && Failure is null;
}

public sealed record ProviderSynthesisFailure(ProviderSynthesisFailureKind Kind, string Message);

public enum ProviderSynthesisFailureKind
{
    ProviderUnavailable,
    InvalidRequest,
    Network,
    Timeout,
    InvalidAudio,
    Cancelled,
    Unknown
}

public sealed record ProviderRuntimeResolution(
    SpeechProviderInstance? Provider,
    IProviderRuntime? Runtime,
    ProviderRuntimeUnavailableReason? UnavailableReason)
{
    public bool IsAvailable => Provider is not null && Runtime is not null && UnavailableReason is null;
}

public enum ProviderRuntimeUnavailableReason
{
    NoCurrentProvider,
    ProviderNotFound,
    ProviderNotConfigured,
    UnsupportedProviderType,
    RuntimeUnavailable
}
