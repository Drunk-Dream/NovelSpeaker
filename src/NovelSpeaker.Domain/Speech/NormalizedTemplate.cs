namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Represents a validated template that mixes literal text with JavaScript expressions.
/// </summary>
public sealed record NormalizedTemplate(
    string RawText,
    IReadOnlyList<TemplateSegment> Segments)
{
    private const int MaxExpressionLength = 2048;

    public static NormalizedTemplate Parse(string templateText)
    {
        ArgumentNullException.ThrowIfNull(templateText);

        var segments = new List<TemplateSegment>();
        var cursor = 0;

        while (cursor < templateText.Length)
        {
            var openIndex = templateText.IndexOf("{{", cursor, StringComparison.Ordinal);
            if (openIndex < 0)
            {
                if (cursor < templateText.Length)
                {
                    segments.Add(new LiteralTemplateSegment(templateText[cursor..]));
                }

                return new NormalizedTemplate(templateText, segments);
            }

            if (openIndex > cursor)
            {
                segments.Add(new LiteralTemplateSegment(templateText[cursor..openIndex]));
            }

            var closeIndex = templateText.IndexOf("}}", openIndex + 2, StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                throw new FormatException("模板中存在未闭合的 {{ ... }} 表达式。");
            }

            var expression = templateText[(openIndex + 2)..closeIndex].Trim();
            if (expression.Length == 0)
            {
                throw new FormatException("模板表达式不能为空。");
            }

            if (expression.Length > MaxExpressionLength)
            {
                throw new FormatException($"模板表达式长度不能超过 {MaxExpressionLength} 个字符。");
            }

            segments.Add(new ExpressionTemplateSegment(expression));
            cursor = closeIndex + 2;
        }

        return new NormalizedTemplate(templateText, segments);
    }
}
