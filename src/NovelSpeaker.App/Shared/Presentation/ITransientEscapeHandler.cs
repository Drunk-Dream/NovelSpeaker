namespace NovelSpeaker.App.Shared.Presentation;

/// <summary>
/// Consumes Escape for a page-local transient interaction before shell navigation runs.
/// </summary>
public interface ITransientEscapeHandler
{
    bool TryHandleEscape();
}
