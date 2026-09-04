namespace NovelSpeaker.App.Features.Books.Shared;

public interface IBookCatalogInvalidationState
{
    bool IsInvalidated { get; }

    void Invalidate();

    void Consume();
}
