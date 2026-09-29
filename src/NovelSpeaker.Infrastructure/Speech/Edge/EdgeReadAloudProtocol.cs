using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Infrastructure.Speech.Edge;

/// <summary>Connection compatibility profile pinned to the shipped application, independent of audio identity.</summary>
internal sealed record EdgeReadAloudProfile(
    string VoiceEndpoint, string WebSocketEndpoint, string TrustedClientToken,
    string ChromiumVersion, string Origin, string UserAgent)
{
    public const string Version = "edge-readaloud-144-v1";
    public static EdgeReadAloudProfile Default { get; } = new(
        "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list",
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1",
        "6A5AA1D4EAFF4E9FB37E23D68491D6F4", "144.0.3719.82",
        "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36 Edg/144.0.0.0");
}

internal static class EdgeReadAloudProtocol
{
    public const string OutputFormat = "audio-24khz-96kbitrate-mono-mp3";
    public static string GenerateGec(DateTimeOffset utcNow, string token)
    {
        var ticks = utcNow.ToFileTime();
        ticks -= ticks % 3_000_000_000L;
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(
            ticks.ToString(CultureInfo.InvariantCulture) + token)));
    }

    public static IReadOnlyList<EdgeVoice> ParseVoices(string json)
    {
        using var document = JsonDocument.Parse(json);
        var voices = new List<EdgeVoice>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var voice = new EdgeVoice(item.GetProperty("Name").GetString()!,
                item.GetProperty("FriendlyName").GetString()!, item.GetProperty("Locale").GetString()!,
                item.GetProperty("Gender").GetString()!);
            if (string.IsNullOrWhiteSpace(voice.VoiceId) || string.IsNullOrWhiteSpace(voice.FriendlyName) ||
                string.IsNullOrWhiteSpace(voice.Locale) || string.IsNullOrWhiteSpace(voice.Gender))
                throw new InvalidDataException("Voice metadata is incomplete.");
            voices.Add(voice);
        }
        if (voices.Count == 0) throw new InvalidDataException("Voice catalog is empty.");
        return voices.AsReadOnly();
    }

    public static string CreateSsml(EdgeVoice voice, string text, int ratePercent)
    {
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        return new XElement(ns + "speak", new XAttribute("version", "1.0"),
            new XAttribute(XNamespace.Xml + "lang", voice.Locale),
            new XElement(ns + "voice", new XAttribute("name", voice.VoiceId),
                new XElement(ns + "prosody", new XAttribute("rate",
                    ratePercent.ToString("+0;-0;0", CultureInfo.InvariantCulture) + "%"), text)))
            .ToString(SaveOptions.DisableFormatting);
    }

    public static string CreateConfig(DateTimeOffset now) =>
        $"X-Timestamp:{Timestamp(now)}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
        "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"" + OutputFormat + "\"}}}}";

    public static string CreateSsmlMessage(string requestId, EdgeVoice voice, string text, int rate, DateTimeOffset now) =>
        $"X-Timestamp:{Timestamp(now)}\r\nX-RequestId:{requestId}\r\nContent-Type:application/ssml+xml\r\nPath:ssml\r\n\r\n" +
        CreateSsml(voice, text, rate);

    private static string Timestamp(DateTimeOffset now) =>
        now.UtcDateTime.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'", CultureInfo.InvariantCulture);

    public static bool IsTurnEnd(string message, string requestId)
    {
        var separator = message.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (separator < 0) throw new InvalidDataException("Text frame has no header boundary.");
        var headers = ParseHeaders(message[..separator]);
        return headers.GetValueOrDefault("X-RequestId") == requestId && headers.GetValueOrDefault("Path") == "turn.end";
    }

    public static ReadOnlyMemory<byte> AudioPayload(byte[] message, string requestId)
    {
        if (message.Length < 2) throw new InvalidDataException("Binary frame is truncated.");
        var length = BinaryPrimitives.ReadUInt16BigEndian(message);
        if (length > message.Length - 2) throw new InvalidDataException("Binary frame header is truncated.");
        var headers = ParseHeaders(Encoding.UTF8.GetString(message, 2, length));
        if (headers.GetValueOrDefault("Path") != "audio" || headers.GetValueOrDefault("X-RequestId") != requestId)
            return ReadOnlyMemory<byte>.Empty;
        return message.AsMemory(length + 2);
    }

    private static Dictionary<string, string> ParseHeaders(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) throw new InvalidDataException("Malformed frame header.");
            result[line[..separator]] = line[(separator + 1)..].Trim();
        }
        if (!result.ContainsKey("Path") || !result.ContainsKey("X-RequestId"))
            throw new InvalidDataException("Frame identity is missing.");
        return result;
    }
}
