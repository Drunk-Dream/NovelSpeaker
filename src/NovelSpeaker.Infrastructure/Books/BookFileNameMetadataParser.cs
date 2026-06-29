namespace NovelSpeaker.Infrastructure.Books;

/// <summary>
/// Parses a file name against a lightweight template to derive book metadata.
/// </summary>
public sealed class BookFileNameMetadataParser
{
    private const string NamePlaceholder = "name";
    private const string AuthorPlaceholder = "author";

    public BookFileNameMetadataParseResult Parse(string fileNameWithoutExtension, string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(template);

        if (template.Length == 0)
        {
            return CreateFallback(fileNameWithoutExtension);
        }

        if (!TryTokenize(template, out var tokens))
        {
            return CreateFallback(fileNameWithoutExtension);
        }

        if (!TryMatch(fileNameWithoutExtension, tokens, out var captures))
        {
            return CreateFallback(fileNameWithoutExtension);
        }

        var title = captures[NamePlaceholder].Trim();
        if (title.Length == 0)
        {
            return CreateFallback(fileNameWithoutExtension);
        }

        captures.TryGetValue(AuthorPlaceholder, out var author);
        var normalizedAuthor = string.IsNullOrWhiteSpace(author)
            ? null
            : author.Trim();

        return new BookFileNameMetadataParseResult(title, normalizedAuthor, true);
    }

    private static BookFileNameMetadataParseResult CreateFallback(string fileNameWithoutExtension)
    {
        return new BookFileNameMetadataParseResult(fileNameWithoutExtension, null, false);
    }

    private static bool TryTokenize(string template, out List<TemplateToken> tokens)
    {
        tokens = [];
        var placeholders = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        while (index < template.Length)
        {
            var openIndex = template.IndexOf("{{", index, StringComparison.Ordinal);
            if (openIndex < 0)
            {
                AddLiteralToken(template[index..], tokens);
                break;
            }

            AddLiteralToken(template[index..openIndex], tokens);

            var closeIndex = template.IndexOf("}}", openIndex + 2, StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                return false;
            }

            var placeholderName = template[(openIndex + 2)..closeIndex].Trim();
            if (!IsSupportedPlaceholder(placeholderName) || !placeholders.Add(placeholderName))
            {
                return false;
            }

            if (tokens.Count > 0 && tokens[^1].Kind == TemplateTokenKind.Placeholder)
            {
                return false;
            }

            tokens.Add(new TemplateToken(TemplateTokenKind.Placeholder, placeholderName));
            index = closeIndex + 2;
        }

        return placeholders.Contains(NamePlaceholder);
    }

    private static void AddLiteralToken(string value, List<TemplateToken> tokens)
    {
        if (value.Length == 0)
        {
            return;
        }

        tokens.Add(new TemplateToken(TemplateTokenKind.Literal, value));
    }

    private static bool TryMatch(
        string fileNameWithoutExtension,
        IReadOnlyList<TemplateToken> tokens,
        out Dictionary<string, string> captures)
    {
        captures = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;

        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            if (token.Kind == TemplateTokenKind.Literal)
            {
                if (!fileNameWithoutExtension.AsSpan(index).StartsWith(token.Value, StringComparison.Ordinal))
                {
                    return false;
                }

                index += token.Value.Length;
                continue;
            }

            var nextLiteral = GetNextLiteral(tokens, tokenIndex + 1);
            if (nextLiteral is null)
            {
                captures[token.Value] = fileNameWithoutExtension[index..];
                index = fileNameWithoutExtension.Length;
                continue;
            }

            var nextLiteralIndex = fileNameWithoutExtension.IndexOf(nextLiteral, index, StringComparison.Ordinal);
            if (nextLiteralIndex < 0)
            {
                return false;
            }

            captures[token.Value] = fileNameWithoutExtension[index..nextLiteralIndex];
            index = nextLiteralIndex;
        }

        return index == fileNameWithoutExtension.Length;
    }

    private static string? GetNextLiteral(IReadOnlyList<TemplateToken> tokens, int startIndex)
    {
        for (var index = startIndex; index < tokens.Count; index++)
        {
            if (tokens[index].Kind == TemplateTokenKind.Literal)
            {
                return tokens[index].Value;
            }
        }

        return null;
    }

    private static bool IsSupportedPlaceholder(string value)
    {
        return string.Equals(value, NamePlaceholder, StringComparison.Ordinal) ||
            string.Equals(value, AuthorPlaceholder, StringComparison.Ordinal);
    }

    private enum TemplateTokenKind
    {
        Literal,
        Placeholder
    }

    private sealed record TemplateToken(TemplateTokenKind Kind, string Value);
}
