namespace NovelSpeaker.Application.Speech.Compilation;

/// <summary>The read-only variables exposed to HTTP Provider templates.</summary>
public sealed record SpeechTemplateContext(string SpeakText, int SpeakSpeed);
