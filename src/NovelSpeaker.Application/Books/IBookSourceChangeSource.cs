namespace NovelSpeaker.Application.Books;

/// <summary>Committed source changes published by the Books owner.</summary>
public interface IBookSourceChangeSource
{
    event EventHandler<BookSourceCatalogChanged>? CatalogChanged;
}

public sealed record BookSourceCatalogChanged(string BookId, string SourceId, string CatalogVersion);
