namespace NovelSpeaker.App.Dialogs;

public sealed record BookDeleteDialogResult(
    bool IsConfirmed,
    bool DeleteAudioCache);
