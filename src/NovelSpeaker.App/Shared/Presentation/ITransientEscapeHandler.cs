namespace NovelSpeaker.App.Shared.Presentation;

/// <summary>
/// Lets the active navigation page consume Escape before text-editing suppression or
/// shell navigation. The page bridges to its existing interaction state owner.
/// </summary>
public interface ITransientEscapeHandler
{
    bool TryHandleEscape();
}
