using System.Text;

namespace NovelSpeaker.Domain.Books;

/// <summary>
/// The exact product identity shared by import, lookup and persistence migration.
/// Case, punctuation, brackets and subtitles remain significant.
/// </summary>
public sealed record BookIdentity
{
    private BookIdentity(string normalizedTitle, string normalizedAuthor)
    {
        NormalizedTitle = normalizedTitle;
        NormalizedAuthor = normalizedAuthor;
    }

    public string NormalizedTitle { get; }
    public string NormalizedAuthor { get; }

    public static BookIdentity Create(string title, string? author)
    {
        ArgumentNullException.ThrowIfNull(title);
        var normalizedTitle = Normalize(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedTitle, nameof(title));
        return new BookIdentity(normalizedTitle, Normalize(author ?? ""));
    }

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormC);
        var result = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
                result.Append(' ');
            result.Append(character);
            pendingSpace = false;
        }

        return result.ToString();
    }
}
