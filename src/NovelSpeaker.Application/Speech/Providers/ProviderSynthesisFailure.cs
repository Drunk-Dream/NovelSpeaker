namespace NovelSpeaker.Application.Speech.Providers;

public sealed record ProviderSynthesisFailure(ProviderSynthesisFailureKind Kind, string Message);
