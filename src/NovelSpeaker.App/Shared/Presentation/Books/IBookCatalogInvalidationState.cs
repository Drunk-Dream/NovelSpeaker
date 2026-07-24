namespace NovelSpeaker.App.Shared.Presentation.Books;

public interface IBookCatalogInvalidationState
{
    bool IsInvalidated { get; }

    void Invalidate();

    void Consume();
}
