namespace NovelSpeaker.App.Dialogs;

public sealed record BookDeleteDialogRequest(
    string BookTitle,
    bool IsCurrentPlaybackBook,
    bool DeleteAudioCacheByDefault = true);
