namespace NovelSpeaker.Application.Books.Import;

internal sealed class BookImportIdGenerator : IBookImportIdGenerator
{
    public string CreateBookId() => Guid.NewGuid().ToString();

    public string CreateChapterId() => Guid.NewGuid().ToString();
}
