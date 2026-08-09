using System.Text.Encodings.Web;
using System.Text.Json;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

internal static class TtsRuleJsonSerializer
{
    public static string Serialize(HttpTtsRule rule)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        writer.WriteString("name", rule.Name);
        writer.WriteString("url", rule.Url);
        writer.WriteBoolean("isEnabled", rule.IsEnabled);
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
            writer.WriteString("header", JsonSerializer.Serialize(rule.Headers));
        }

        if (!string.IsNullOrWhiteSpace(rule.RequestMethod) || !string.IsNullOrWhiteSpace(rule.RequestBody))
        {
            writer.WritePropertyName("requestOptions");
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(rule.RequestMethod))
            {
                writer.WriteString("method", rule.RequestMethod);
            }

            if (!string.IsNullOrWhiteSpace(rule.RequestBody))
            {
                writer.WritePropertyName("body");
                if (rule.RequestBodyIsJsonStructure)
                {
                    using var document = JsonDocument.Parse(rule.RequestBody);
                    document.RootElement.WriteTo(writer);
                }
                else
                {
                    writer.WriteStringValue(rule.RequestBody);
                }
            }

            writer.WriteEndObject();
        }

        if (rule.LastUpdateTime is long lastUpdate)
        {
            writer.WriteNumber("lastUpdateTime", lastUpdate);
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
