namespace NovelSpeaker.App.Features.Books.Shared;

public sealed record BookDeleteDialogResult(
    bool IsConfirmed,
    bool DeleteAudioCache);
