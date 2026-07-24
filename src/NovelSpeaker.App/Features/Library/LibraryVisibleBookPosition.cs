namespace NovelSpeaker.App.Features.Library;

public sealed record LibraryVisibleBookPosition(
    string BookId,
    double Top,
    double Bottom);
