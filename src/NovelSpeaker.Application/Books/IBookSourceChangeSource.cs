namespace NovelSpeaker.Application.Books;

/// <summary>Committed source changes published by the Books owner.</summary>
public interface IBookSourceChangeSource
{
    event EventHandler<BookCommittedChange>? Changed;
}
