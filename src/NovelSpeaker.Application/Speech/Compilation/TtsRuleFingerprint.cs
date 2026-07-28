using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.Application.Speech.Compilation;

/// <summary>
/// Versioned identity of the effective HTTP request contract of a TTS rule.
/// </summary>
public sealed record TtsRuleFingerprint(
    int SchemaVersion,
    Fingerprint Value)
{
    public const int CurrentSchemaVersion = 1;
    public const int ExecutionContractVersion = 1;

    public ReadOnlyMemory<byte> Bytes => Value.Bytes;

    public string Hex => Value.Hex;

    public static TtsRuleFingerprint Create(NormalizedHttpTtsRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var writer = new CanonicalIdentityWriter();
        writer.Add("schema", CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("execution-contract", ExecutionContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("url", NormalizeUrlTemplate(rule.UrlTemplate));

        var hasBody = rule.RequestBodyTemplate is not null &&
            !string.IsNullOrWhiteSpace(rule.RequestBodyTemplate.RawText);
        var method = string.IsNullOrWhiteSpace(rule.RequestMethod)
            ? hasBody ? "POST" : "GET"
            : rule.RequestMethod.Trim().ToUpperInvariant();
        writer.Add("method", method);
        writer.Add("json-structure", rule.RequestBodyIsJsonStructure ? "1" : "0");
        writer.Add("content-type", rule.DeclaredContentType?.Trim().ToLowerInvariant());
        writer.Add("body", rule.RequestBodyTemplate is null
            ? string.Empty
            : NormalizeTemplate(rule.RequestBodyTemplate));

        foreach (var header in rule.HeaderTemplates
                     .OrderBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            writer.Add("header-name", header.Key.Trim().ToLowerInvariant());
            writer.Add("header-value", NormalizeTemplate(header.Value));
        }

        return new TtsRuleFingerprint(CurrentSchemaVersion, writer.Build());
    }

    private static string NormalizeUrlTemplate(NormalizedTemplate template)
    {
        var normalized = CanonicalTemplate(template);
        if (template.Segments.Count == 1 && template.Segments[0] is LiteralTemplateSegment literal)
        {
            var value = literal.Text.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return uri.AbsoluteUri;
            }
        }

        return normalized;
    }

    private static string NormalizeTemplate(NormalizedTemplate template)
    {
        var writer = new CanonicalIdentityWriter();
        writer.Add("template", CanonicalTemplate(template));
        return writer.Build().Hex;
    }

    private static string CanonicalTemplate(NormalizedTemplate template)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var segment in template.Segments)
        {
            switch (segment)
            {
                case LiteralTemplateSegment literal:
                    Append(builder, "literal", literal.Text);
                    break;
                case ExpressionTemplateSegment expression:
                    Append(builder, "expression", expression.Expression.Trim());
                    break;
                default:
                    throw new InvalidOperationException("未知的 TTS 模板片段类型。");
            }
        }

        return builder.ToString();
    }

    private static void Append(System.Text.StringBuilder builder, string kind, string value)
    {
        builder.Append(kind).Append(':').Append(value.Length).Append(':').Append(value).Append(';');
    }
}
