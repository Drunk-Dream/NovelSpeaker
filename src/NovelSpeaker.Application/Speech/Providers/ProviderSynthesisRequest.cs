using NovelSpeaker.Application.Speech;

namespace NovelSpeaker.Application.Speech.Providers;

public sealed record ProviderSynthesisRequest(
    string Text,
    int SpeakSpeed,
    TtsAdmissionPriority Priority = TtsAdmissionPriority.CurrentPlayback);
