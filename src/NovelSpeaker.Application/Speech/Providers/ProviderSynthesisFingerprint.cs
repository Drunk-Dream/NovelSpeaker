using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public sealed record ProviderSynthesisFingerprint(int SchemaVersion, Fingerprint Value)
{
    public const int CurrentSchemaVersion = 1;
    public const int HttpExecutionContractVersion = 1;
    public const int EdgeSynthesisContractVersion = 1;

    public ReadOnlyMemory<byte> Bytes => Value.Bytes;

    public string Hex => Value.Hex;

    public static ProviderSynthesisFingerprint Create(SpeechProviderInstance provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.Configuration switch
        {
            HttpSpeechProviderConfiguration http => Create(http),
            EdgeSpeechProviderConfiguration edge => Create(edge),
            _ => throw new NotSupportedException($"Synthesis fingerprint is not defined for {provider.Type}.")
        };
    }

    public static ProviderSynthesisFingerprint Create(HttpSpeechProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var writer = new CanonicalIdentityWriter();
        writer.Add("schema", CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("provider-type", "http");
        writer.Add("execution-contract", HttpExecutionContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("url", NormalizeUrl(configuration.UrlTemplate));
        writer.Add("method", configuration.Method.Trim().ToUpperInvariant());
        writer.Add("body", CanonicalTemplate(configuration.BodyTemplate ?? string.Empty));

        foreach (var header in configuration.Headers
                     .OrderBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(pair => pair.Key.Trim(), StringComparer.Ordinal))
        {
            writer.Add("header-name", header.Key.Trim().ToLowerInvariant());
            writer.Add("header-value", CanonicalTemplate(header.Value));
        }

        return new ProviderSynthesisFingerprint(CurrentSchemaVersion, writer.Build());
    }

    public static ProviderSynthesisFingerprint Create(EdgeSpeechProviderConfiguration configuration,
        int contractVersion = EdgeSynthesisContractVersion)
    {
        var writer = new CanonicalIdentityWriter();
        writer.Add("schema", CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("provider-type", "microsoft-edge");
        writer.Add("synthesis-contract", contractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("voice", configuration.Voice?.VoiceId ?? string.Empty);
        return new ProviderSynthesisFingerprint(CurrentSchemaVersion, writer.Build());
    }

    private static string NormalizeUrl(string template)
    {
        var normalized = CanonicalTemplate(template);
        if (!template.Contains("{{", StringComparison.Ordinal) &&
            Uri.TryCreate(template.Trim(), UriKind.Absolute, out var uri))
        {
            return uri.AbsoluteUri;
        }

        return normalized;
    }

    private static string CanonicalTemplate(string template)
    {
        var parsed = NormalizedTemplate.Parse(template);
        var builder = new System.Text.StringBuilder();
        foreach (var segment in parsed.Segments)
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
                    throw new InvalidOperationException("Unknown HTTP Provider template segment.");
            }
        }

        return builder.ToString();
    }

    private static void Append(System.Text.StringBuilder builder, string kind, string value) =>
        builder.Append(kind).Append(':').Append(value.Length).Append(':').Append(value).Append(';');
}
