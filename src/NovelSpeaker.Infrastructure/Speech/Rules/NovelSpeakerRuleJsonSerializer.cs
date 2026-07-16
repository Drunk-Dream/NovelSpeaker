using System.Text.Encodings.Web;
using System.Text.Json;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal static class NovelSpeakerRuleJsonSerializer
{
    public static string Serialize(HttpTtsRule rule)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        writer.WriteStartObject();
        writer.WriteString("name", rule.Name);
        writer.WriteString("url", rule.Url);

        if (!string.IsNullOrWhiteSpace(rule.ContentType))
        {
            writer.WriteString("contentType", rule.ContentType);
        }

        if (!string.IsNullOrWhiteSpace(rule.ConcurrentRate))
        {
            writer.WriteString("concurrentRate", rule.ConcurrentRate);
        }

        if (rule.Headers.Count > 0)
        {
            writer.WriteString("header", TtsRulePersistenceMapper.SerializeHeaders(rule.Headers));
        }

        var requestOptionsJson = TtsRulePersistenceMapper.SerializeRequestOptions(
            rule.RequestMethod,
            rule.RequestBody,
            rule.RequestBodyIsJsonStructure);
        if (!string.IsNullOrWhiteSpace(requestOptionsJson))
        {
            writer.WritePropertyName("requestOptions");
            using var document = JsonDocument.Parse(requestOptionsJson);
            document.RootElement.WriteTo(writer);
        }

        if (rule.LastUpdateTime is long lastUpdateTime)
        {
            writer.WriteNumber("lastUpdateTime", lastUpdateTime);
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
