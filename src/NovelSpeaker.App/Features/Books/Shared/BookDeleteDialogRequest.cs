namespace NovelSpeaker.App.Features.Books.Shared;

public sealed record BookDeleteDialogRequest(
    string BookTitle,
    bool IsCurrentPlaybackBook,
    int BookCount = 1);
