using System.Text;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.Text;

/// <summary>
/// Normalizes newlines and removes unsupported control characters.
/// </summary>
public sealed class TextNormalizer : ITextNormalizer
{
    public string Normalize(string rawText)
    {
        var unified = rawText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var builder = new StringBuilder(unified.Length);
        foreach (var character in unified)
        {
            if (character == '\n' || character == '\t' || !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
