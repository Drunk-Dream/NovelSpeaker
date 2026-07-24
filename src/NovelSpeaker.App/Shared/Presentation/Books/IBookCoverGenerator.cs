namespace NovelSpeaker.App.Shared.Presentation.Books;

public interface IBookCoverGenerator
{
    GeneratedBookCover Generate(string title);
}
