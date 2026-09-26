using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelSpeaker.Domain.Speech.Providers;

[JsonConverter(typeof(ProviderIdJsonConverter))]
public readonly record struct ProviderId(Guid Value)
{
    public static ProviderId New() => new(Guid.NewGuid());

    public static ProviderId FromLegacyHttpTtsRuleId(long ruleId)
    {
        Span<byte> input = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(input, ruleId);
        var prefix = Encoding.UTF8.GetBytes("NovelSpeaker.LegacyHttpTtsRule.ProviderId.v1:");
        var identity = new byte[prefix.Length + input.Length];
        prefix.CopyTo(identity, 0);
        input.CopyTo(identity.AsSpan(prefix.Length));
        var hash = SHA256.HashData(identity);
        return new ProviderId(new Guid(hash.AsSpan(0, 16)));
    }

    public override string ToString() => Value.ToString("D");
}

public sealed class ProviderIdJsonConverter : JsonConverter<ProviderId>
{
    public override ProviderId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !Guid.TryParse(reader.GetString(), out var value))
        {
            throw new JsonException("ProviderId must be a GUID string.");
        }

        return new ProviderId(value);
    }

    public override void Write(Utf8JsonWriter writer, ProviderId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
