namespace NovelSpeaker.App.Shared.Presentation.Platform;

public sealed record PresentationFolderDialogOptions(
    string Title,
    string? InitialDirectory = null);
