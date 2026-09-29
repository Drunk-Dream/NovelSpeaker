using System.Text.Json;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Versioned identity of every configuration that can change generated audio.
/// </summary>
public sealed record SynthesisProfileFingerprint(
    int SchemaVersion,
    ProviderSynthesisFingerprint Provider,
    int SpeakSpeed,
    string? OptionsJson,
    Fingerprint Value)
{
    public const int CurrentSchemaVersion = 2;

    public ReadOnlyMemory<byte> Bytes => Value.Bytes;

    public string Hex => Value.Hex;

    public static SynthesisProfileFingerprint Create(
        ProviderSynthesisFingerprint provider,
        int speakSpeed,
        string? optionsJson = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var normalizedOptions = NormalizeOptionsJson(optionsJson);
        var writer = new CanonicalIdentityWriter();
        writer.Add("schema", CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("provider-synthesis", provider.Hex);
        writer.Add("speak-speed", speakSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Add("options", normalizedOptions);
        return new SynthesisProfileFingerprint(
            CurrentSchemaVersion,
            provider,
            speakSpeed,
            normalizedOptions,
            writer.Build());
    }

    private static string? NormalizeOptionsJson(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(optionsJson);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
