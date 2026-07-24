namespace NovelSpeaker.App.Features.BookDetails;

public sealed record BookDeleteDialogResult(
    bool IsConfirmed,
    bool DeleteAudioCache);
