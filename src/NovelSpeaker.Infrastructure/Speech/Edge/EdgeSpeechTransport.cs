using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.Speech.Http;

namespace NovelSpeaker.Infrastructure.Speech.Edge;

/// <summary>Uses independent, bounded WebSocket turns and validates MP3 with the production decoder.</summary>
public sealed class EdgeSpeechTransport : IEdgeSpeechTransport, IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    private const int MaxMessageBytes = 1024 * 1024;
    private const int MaxAudioBytes = 32 * 1024 * 1024;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly EdgeReadAloudProfile _profile;
    private readonly TimeProvider _time;
    private readonly TemporaryAudioStore _temporary;
    private readonly AudioProbe _probe;

    public EdgeSpeechTransport(TimeProvider timeProvider, TemporaryAudioStore temporary, AudioProbe probe)
        : this(timeProvider, temporary, probe, EdgeReadAloudProfile.Default) { }

    internal EdgeSpeechTransport(TimeProvider timeProvider, TemporaryAudioStore temporary, AudioProbe probe,
        EdgeReadAloudProfile profile)
    {
        _time = timeProvider;
        _temporary = temporary;
        _probe = probe;
        _profile = profile;
    }

    public async Task<IReadOnlyList<EdgeVoice>> GetVoicesAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                _profile.VoiceEndpoint + "?trustedclienttoken=" + _profile.TrustedClientToken);
            request.Headers.TryAddWithoutValidation("User-Agent", _profile.UserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int count;
            while ((count = await content.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + count > 4 * MaxMessageBytes) throw new InvalidDataException();
                body.Write(buffer, 0, count);
            }
            return await Task.Run(() => EdgeReadAloudProtocol.ParseVoices(Encoding.UTF8.GetString(body.ToArray())), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new TimeoutException("Voice 列表请求超时，请重试。"); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Discard protocol exceptions: their messages can include URLs and tokens.
            throw new InvalidOperationException("Voice 列表获取失败，请重试。");
        }
    }

    public async Task<ProviderSynthesisResult> SynthesizeAsync(EdgeVoice voice, string text, int ratePercent,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);
        using var socket = new ClientWebSocket();
        using var abort = timeout.Token.Register(socket.Abort);
        MemoryStream? audio = null;
        try
        {
            var now = _time.GetUtcNow();
            var requestId = Guid.NewGuid().ToString("N");
            var url = _profile.WebSocketEndpoint + "?TrustedClientToken=" + _profile.TrustedClientToken +
                "&Sec-MS-GEC=" + EdgeReadAloudProtocol.GenerateGec(now, _profile.TrustedClientToken) +
                "&Sec-MS-GEC-Version=1-" + _profile.ChromiumVersion + "&ConnectionId=" + Guid.NewGuid().ToString("N");
            socket.Options.SetRequestHeader("Origin", _profile.Origin);
            socket.Options.SetRequestHeader("User-Agent", _profile.UserAgent);
            await socket.ConnectAsync(new Uri(url), timeout.Token).ConfigureAwait(false);
            await SendAsync(socket, EdgeReadAloudProtocol.CreateConfig(now), timeout.Token).ConfigureAwait(false);
            await SendAsync(socket, EdgeReadAloudProtocol.CreateSsmlMessage(requestId, voice, text, ratePercent, now), timeout.Token).ConfigureAwait(false);
            audio = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult received;
                WebSocketMessageType? messageType = null;
                do
                {
                    received = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
                    if (received.MessageType == WebSocketMessageType.Close)
                        return Failure(ProviderSynthesisFailureKind.Network, "Microsoft Edge 合成连接提前关闭。");
                    if (messageType is not null && messageType != received.MessageType) throw new InvalidDataException();
                    messageType = received.MessageType;
                    if (message.Length + received.Count > MaxMessageBytes) throw new InvalidDataException();
                    message.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                if (received.MessageType == WebSocketMessageType.Binary)
                {
                    var payload = EdgeReadAloudProtocol.AudioPayload(message.ToArray(), requestId);
                    if (audio.Length + payload.Length > MaxAudioBytes) throw new InvalidDataException();
                    await audio.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
                }
                else if (EdgeReadAloudProtocol.IsTurnEnd(Encoding.UTF8.GetString(message.ToArray()), requestId)) break;
            }
            if (audio.Length == 0) return Failure(ProviderSynthesisFailureKind.InvalidAudio, "Microsoft Edge 返回了空音频。");
            audio.Position = 0;
            string? temporary = null;
            string? candidate = null;
            try
            {
                temporary = await _temporary.WriteAsync(0, audio, timeout.Token).ConfigureAwait(false);
                candidate = _temporary.CreateCandidate(temporary, "mp3");
                var valid = await Task.Run(() => _probe.CanDecode(candidate), timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (!valid) return Failure(ProviderSynthesisFailureKind.InvalidAudio, "Microsoft Edge 音频无法解码。");
            }
            finally
            {
                TemporaryAudioStore.Delete(candidate);
                TemporaryAudioStore.Delete(temporary);
            }
            audio.Position = 0;
            var result = new ProviderSynthesisResult(audio, "audio/mpeg", null, "mp3");
            audio = null;
            return result;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            return Failure(ProviderSynthesisFailureKind.Timeout, "Microsoft Edge 合成超时，请重试。");
        }
        catch (Exception exception) when (exception is WebSocketException or HttpRequestException)
        {
            return Failure(ProviderSynthesisFailureKind.Network, "Microsoft Edge 连接失败，请重试。");
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException)
        {
            return Failure(ProviderSynthesisFailureKind.InvalidAudio, "Microsoft Edge 合成响应无效。");
        }
        finally { audio?.Dispose(); }
    }

    private static Task SendAsync(ClientWebSocket socket, string message, CancellationToken cancellationToken) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, cancellationToken);

    private static ProviderSynthesisResult Failure(ProviderSynthesisFailureKind kind, string message) =>
        new(null, null, new ProviderSynthesisFailure(kind, message));

    public void Dispose() => _http.Dispose();
}
