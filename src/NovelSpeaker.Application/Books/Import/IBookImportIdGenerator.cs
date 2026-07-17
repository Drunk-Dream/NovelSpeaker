namespace NovelSpeaker.Application.Books.Import;

/// <summary>
/// Creates stable identifiers for a book, its chapters, and the durable import operation.
/// </summary>
public interface IBookImportIdGenerator
{
    string CreateBookId();

    string CreateChapterId();

    string CreateOperationId();
}
