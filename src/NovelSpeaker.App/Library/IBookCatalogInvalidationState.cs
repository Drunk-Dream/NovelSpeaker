namespace NovelSpeaker.App.Library;

public interface IBookCatalogInvalidationState
{
    bool IsInvalidated { get; }

    void Invalidate();

    void Consume();
}
