namespace NovelSpeaker.App.Library;

public interface IBookCoverGenerator
{
    GeneratedBookCover Generate(string title);
}
