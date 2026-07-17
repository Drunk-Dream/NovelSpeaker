namespace NovelSpeaker.Application.Books.Import;

/// <summary>
/// Creates stable identifiers for books and chapters during one import operation.
/// </summary>
public interface IBookImportIdGenerator
{
    string CreateBookId();

    string CreateChapterId();
}
