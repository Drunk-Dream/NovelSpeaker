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

        if (!string.IsNullOrWhiteSpace(rule.Header))
        {
            writer.WriteString("header", rule.Header);
        }

        if (!string.IsNullOrWhiteSpace(rule.RequestOptionsJson))
        {
            writer.WritePropertyName("requestOptions");
            using var document = JsonDocument.Parse(rule.RequestOptionsJson);
            document.RootElement.WriteTo(writer);
        }

        if (rule.EnabledCookieJar)
        {
            writer.WriteBoolean("enabledCookieJar", true);
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
