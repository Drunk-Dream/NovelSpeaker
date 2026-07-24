namespace NovelSpeaker.App.Features.BookDetails;

public sealed record BookDeleteDialogRequest(
    string BookTitle,
    bool IsCurrentPlaybackBook,
    bool DeleteAudioCacheByDefault = true);
