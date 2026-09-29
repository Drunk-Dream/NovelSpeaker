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
