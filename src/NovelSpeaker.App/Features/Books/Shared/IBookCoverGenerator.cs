namespace NovelSpeaker.App.Features.Books.Shared;

public interface IBookCoverGenerator
{
    GeneratedBookCover Generate(string title);
}
