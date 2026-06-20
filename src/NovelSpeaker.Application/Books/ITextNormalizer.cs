namespace NovelSpeaker.Application.Books;

/// <summary>
/// Normalizes imported TXT content before chapter splitting.
/// </summary>
public interface ITextNormalizer
{
    string Normalize(string rawText);
}
