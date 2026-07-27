namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Owns one shared per-rule TTS execution permit until the caller finishes the request.
/// </summary>
public interface ITtsAdmissionLease : IAsyncDisposable
{
}
