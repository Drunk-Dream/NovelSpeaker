using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public sealed record ProviderRuntimeResolution(
    SpeechProviderInstance? Provider,
    IProviderRuntime? Runtime,
    ProviderRuntimeUnavailableReason? UnavailableReason)
{
    public bool IsAvailable => Provider is not null && Runtime is not null && UnavailableReason is null;
}
