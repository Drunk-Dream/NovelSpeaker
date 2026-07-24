namespace NovelSpeaker.App.Shared.Presentation.Books;

public sealed class BookCatalogInvalidationState : IBookCatalogInvalidationState
{
    public bool IsInvalidated { get; private set; }

    public void Invalidate()
    {
        IsInvalidated = true;
    }

    public void Consume()
    {
        IsInvalidated = false;
    }
}
