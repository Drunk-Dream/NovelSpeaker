namespace NovelSpeaker.App.Features.Books.Library;

public sealed record LibraryVisibleBookPosition(
    string BookId,
    double Top,
    double Bottom);
