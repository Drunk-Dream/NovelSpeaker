using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.Speech.Edge;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Speech;

public sealed class EdgeProtocolTests
{
    // Expected values are calculated by the unmodified upstream TypeScript implementation.
    [Theory]
    [InlineData(0L, "52BC7A17B50DFEC9-28C4394775C8751CD2F6859C030C06DB1489ACAB3B3A7651831FC26895177ED6")]
    [InlineData(299999L, "52BC7A17B50DFEC9-28C4394775C8751CD2F6859C030C06DB1489ACAB3B3A7651831FC26895177ED6")]
    [InlineData(300000L, "F7535A20A262E331-28675BF85C7106BBF20035A78FCB0B6890813EAD5C3B983EF3424931B720BABB")]
    [InlineData(1791849600000L, "A1AADAB47B92925F-CB330956409A6D97AB720599857710569FBF2F12376A3479F0663FD7E8BF2BE1")]
    [InlineData(1791849899999L, "A1AADAB47B92925F-CB330956409A6D97AB720599857710569FBF2F12376A3479F0663FD7E8BF2BE1")]
    [InlineData(1791849900000L, "42A087AD7BB77F5E-00ECA740EF5E74EE71E1709709B79F11B7751E5264AA6FFB378B4A91BCE771A4")]
    public void Gec_v2_matches_reference_across_five_minute_boundaries(long unixMs, string expected)
    {
        Assert.Equal(expected, EdgeReadAloudProtocol.GenerateGecV2(
            DateTimeOffset.FromUnixTimeMilliseconds(unixMs), EdgeReadAloudProfile.Default.TrustedClientToken));
    }

    [Fact]
    public void Synthesis_handshake_uses_new_endpoint_subscription_key_and_v2_authentication()
    {
        var profile = EdgeReadAloudProfile.Default;
        var uri = EdgeReadAloudProtocol.CreateSynthesisUri(profile, DateTimeOffset.UnixEpoch, "abc123");
        Assert.Equal("wss://api.msedgeservices.com/tts/cognitiveservices/websocket/v1", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("?Ocp-Apim-Subscription-Key=" + profile.TrustedClientToken +
            "&Sec-MS-GEC=52BC7A17B50DFEC9-28C4394775C8751CD2F6859C030C06DB1489ACAB3B3A7651831FC26895177ED6" +
            "&Sec-MS-GEC-Version=2-155.0.4283.45&ConnectionId=abc123", uri.Query);
    }

    [Fact]
    public void Catalog_maps_real_voice_identity_and_snapshot_fields()
    {
        var voices = EdgeReadAloudProtocol.ParseVoices("""
            [{"Name":"Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)",
              "ShortName":"zh-CN-XiaoxiaoNeural", "FriendlyName":"Microsoft Xiaoxiao Online (Natural) - Chinese (Mainland)",
              "Locale":"zh-CN", "Gender":"Female", "SuggestedCodec":"audio-24khz-48kbitrate-mono-mp3"}]
            """);
        var voice = Assert.Single(voices);
        Assert.Equal("Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)", voice.VoiceId);
        Assert.Equal("Microsoft Xiaoxiao Online (Natural) - Chinese (Mainland)", voice.FriendlyName);
        Assert.Equal("zh-CN", voice.Locale);
        Assert.Equal("Female", voice.Gender);
    }

    [Fact]
    public void Ssml_escapes_text_and_voice_attributes_without_creating_injected_elements()
    {
        var text = "<voice name='other'> & \"quotes\" 中文";
        var voice = new EdgeVoice("voice\"<&", "label", "zh-CN", "Female");
        var ssml = XElement.Parse(EdgeReadAloudProtocol.CreateSsml(voice, text, 100));
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        var element = Assert.Single(ssml.Elements(ns + "voice"));
        Assert.Equal(voice.VoiceId, element.Attribute("name")!.Value);
        var prosody = Assert.Single(element.Elements(ns + "prosody"));
        Assert.Equal(text, prosody.Value);
        Assert.Empty(prosody.Elements());
        Assert.Equal("+100%", prosody.Attribute("rate")!.Value);
    }

    [Fact]
    public void Frames_use_big_endian_header_length_request_identity_audio_path_and_turn_end()
    {
        const string request = "abc123";
        var header = Encoding.UTF8.GetBytes($"X-RequestId:{request}\r\nPath:audio\r\nX-Padding:{new string('x', 300)}\r\n");
        var frame = new byte[2 + header.Length + 3];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)header.Length);
        header.CopyTo(frame, 2);
        new byte[] { 1, 2, 3 }.CopyTo(frame, 2 + header.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, EdgeReadAloudProtocol.AudioPayload(frame, request).ToArray());
        Assert.Empty(EdgeReadAloudProtocol.AudioPayload(frame, "other").ToArray());
        Assert.Throws<InvalidDataException>(() => EdgeReadAloudProtocol.AudioPayload([1, 0], request));
        Assert.True(EdgeReadAloudProtocol.IsTurnEnd($"X-RequestId:{request}\r\nPath:turn.end\r\n\r\n{{}}", request));
        Assert.False(EdgeReadAloudProtocol.IsTurnEnd($"X-RequestId:other\r\nPath:turn.end\r\n\r\n{{}}", request));
        Assert.False(EdgeReadAloudProtocol.IsTurnEnd($"X-RequestId:{request}\r\nPath:turn.start\r\n\r\n{{}}", request));
    }

    [Fact]
    public void Connection_profile_changes_do_not_enter_synthesis_fingerprint()
    {
        var voice = new EdgeVoice("voice-A", "label", "zh-CN", "Female");
        var configuration = new EdgeSpeechProviderConfiguration(voice);
        var original = ProviderSynthesisFingerprint.Create(configuration);
        var changedProfile = EdgeReadAloudProfile.Default with { ChromiumVersion = "999.0", UserAgent = "future" };
        Assert.NotEqual(EdgeReadAloudProfile.Default, changedProfile);
        Assert.Equal(original, ProviderSynthesisFingerprint.Create(configuration));
    }
}
