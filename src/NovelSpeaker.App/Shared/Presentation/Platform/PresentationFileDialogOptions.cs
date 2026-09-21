namespace NovelSpeaker.App.Shared.Presentation.Platform;

public sealed record PresentationFileDialogOptions(
    string Filter,
    string? SuggestedFileName = null,
    string? InitialDirectory = null,
    Guid? ClientGuid = null);
