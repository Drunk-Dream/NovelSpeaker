using System.Text;

namespace NovelSpeaker.Application.Books.Import;

/// <summary>
/// Normalizes newlines and removes unsupported control characters before chapter recognition.
/// </summary>
public sealed class TextNormalizer : ITextNormalizer
{
    public string Normalize(string rawText)
    {
        var firstChange = 0;
        while (firstChange < rawText.Length && rawText[firstChange] != '\r' &&
               (rawText[firstChange] is '\n' or '\t' || !char.IsControl(rawText[firstChange])))
            firstChange++;
        if (firstChange == rawText.Length) return rawText;

        var builder = new StringBuilder(rawText.Length);
        builder.Append(rawText.AsSpan(0, firstChange));
        for (var index = firstChange; index < rawText.Length; index++)
        {
            var character = rawText[index];
            if (character == '\r')
            {
                builder.Append('\n');
                if (index + 1 < rawText.Length && rawText[index + 1] == '\n') index++;
                continue;
            }
            if (character == '\n' || character == '\t' || !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
