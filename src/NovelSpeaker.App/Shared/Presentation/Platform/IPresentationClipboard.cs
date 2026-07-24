namespace NovelSpeaker.App.Shared.Presentation.Platform;

/// <summary>
/// Provides text-only clipboard operations at the presentation boundary.
/// </summary>
public interface IPresentationClipboard
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken);

    Task SetTextAsync(string text, CancellationToken cancellationToken);
}
