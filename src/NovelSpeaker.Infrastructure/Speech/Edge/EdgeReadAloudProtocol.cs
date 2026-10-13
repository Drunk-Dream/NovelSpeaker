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
    public const string Version = "edge-readaloud-155-v2";
    public string GecVersion => "2-" + ChromiumVersion;
    public static EdgeReadAloudProfile Default { get; } = new(
        "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list",
        "wss://api.msedgeservices.com/tts/cognitiveservices/websocket/v1",
        "6A5AA1D4EAFF4E9FB37E23D68491D6F4", "155.0.4283.45",
        "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36 Edg/155.0.0.0");
}

internal static class EdgeReadAloudProtocol
{
    public const string OutputFormat = "audio-24khz-96kbitrate-mono-mp3";
    public static Uri CreateSynthesisUri(EdgeReadAloudProfile profile, DateTimeOffset now, string connectionId) =>
        new(profile.WebSocketEndpoint + "?Ocp-Apim-Subscription-Key=" + Uri.EscapeDataString(profile.TrustedClientToken) +
            "&Sec-MS-GEC=" + GenerateGecV2(now, profile.TrustedClientToken) +
            "&Sec-MS-GEC-Version=" + profile.GecVersion + "&ConnectionId=" + Uri.EscapeDataString(connectionId));

    // ms-ra-forwarder fix/drudream @ 4936d3e1: prefix + five-minute FILETIME bucket,
    // followed by the little-endian wrapping-u32 patent hash and SHA-256 digest.
    public static string GenerateGecV2(DateTimeOffset utcNow, string token)
    {
        var unixMs = utcNow.ToUnixTimeMilliseconds();
        ArgumentOutOfRangeException.ThrowIfNegative(unixMs);
        if (token.Any(character => character > 127)) throw new ArgumentException("Speech key must be ASCII.", nameof(token));
        var ticks = ((unixMs + 11_644_473_600_000L) / 300_000L) * 3_000_000_000L;
        var input = Encoding.ASCII.GetBytes("Copyright (C) Microsoft Corporation. All rights reserved." +
            ticks.ToString(CultureInfo.InvariantCulture) + token);
        var digest = SHA256.HashData(input);
        Array.Resize(ref input, ((input.Length + 7) / 8) * 8);
        var a = BinaryPrimitives.ReadUInt32LittleEndian(digest) | 1u;
        var b = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(4)) | 1u;
        uint swapLast = 0, swapSum = 0, state = 0, reversibleSum = 0;
        unchecked
        {
            for (var offset = 0; offset < input.Length; offset += 8)
            {
                var x = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(offset));
                var y = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(offset + 4));
                var t = Rotate16(x) * 0xEF0569FBu + x * a;
                var q = Rotate16(t) * 0x76F50F34u;
                q = Rotate16(q) * 0xC3EFEA97u + q;
                swapSum += q;
                t = Rotate16(y) * 0xC31713DBu + y * b;
                q = Rotate16(t) * 0x1881CE3Cu;
                swapLast = Rotate16(q) * 0x35BD1EC9u + q;
                swapSum += swapLast;

                state = (state + x) * a;
                state = Rotate16(state) * 0xCF98B111u;
                state = Rotate16(state) * 0x87085B9Fu;
                state = Rotate16(state) * 0x12CEB96Du;
                state = Rotate16(state) * 0x257E1D83u;
                t = (y + state) * b;
                t = Rotate16(t) * 0xA27416F5u;
                t = Rotate16(t) * 0xD38396FFu;
                t = Rotate16(t);
                reversibleSum += state;
                state = Rotate16(t * 0x7C932B89u) * 0xBFA49F69u;
                reversibleSum += state;
            }
        }
        Span<byte> patentHash = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(patentHash, swapLast ^ state);
        BinaryPrimitives.WriteUInt32LittleEndian(patentHash[4..], swapSum ^ reversibleSum);
        return Convert.ToHexString(patentHash) + "-" + Convert.ToHexString(digest);
    }

    private static uint Rotate16(uint value) => (value << 16) | (value >> 16);

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
