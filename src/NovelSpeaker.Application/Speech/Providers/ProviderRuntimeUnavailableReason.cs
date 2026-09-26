namespace NovelSpeaker.Application.Speech.Providers;

public enum ProviderRuntimeUnavailableReason
{
    NoCurrentProvider,
    ProviderNotFound,
    ProviderNotConfigured,
    UnsupportedProviderType,
    RuntimeUnavailable
}
