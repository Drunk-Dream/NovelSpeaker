namespace NovelSpeaker.Application.Speech.Compilation;

/// <summary>
/// Represents one segment within a parsed template.
/// </summary>
public abstract record TemplateSegment;

/// <summary>
/// A literal section of a template.
/// </summary>
/// <param name="Text">The literal text to emit unchanged.</param>
public sealed record LiteralTemplateSegment(string Text) : TemplateSegment;

/// <summary>
/// A JavaScript expression inside <c>{{ ... }}</c>.
/// </summary>
/// <param name="Expression">The normalized JavaScript expression.</param>
public sealed record ExpressionTemplateSegment(string Expression) : TemplateSegment;
