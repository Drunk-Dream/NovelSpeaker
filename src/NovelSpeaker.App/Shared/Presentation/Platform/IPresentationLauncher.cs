namespace NovelSpeaker.App.Shared.Presentation.Platform;

/// <summary>
/// Opens a trusted file or directory through the desktop shell.
/// </summary>
public interface IPresentationLauncher
{
    Task OpenAsync(string path, CancellationToken cancellationToken);
}
