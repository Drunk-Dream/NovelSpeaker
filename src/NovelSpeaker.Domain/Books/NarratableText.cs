using System.Globalization;
using System.Text;

namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Defines whether text contains lexical content worth sending to a speech service.
/// Whitespace, punctuation, separators, formatting marks, and decorative symbols alone
/// do not form narratable content.
/// </summary>
public static class NarratableText
{
    public static bool HasContent(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.UppercaseLetter or
                UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or
                UnicodeCategory.ModifierLetter or
                UnicodeCategory.OtherLetter or
                UnicodeCategory.DecimalDigitNumber or
                UnicodeCategory.LetterNumber or
                UnicodeCategory.OtherNumber)
            {
                return true;
            }
        }

        return false;
    }
}
