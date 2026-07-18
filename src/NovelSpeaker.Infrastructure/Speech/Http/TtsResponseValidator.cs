using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Security;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>Classifies HTTP responses, stores payloads temporarily, and validates audio with the decoder.</summary>
public sealed class TtsResponseValidator : ITtsResponseValidator
{
    private const int MaxSummaryBytes = 4096;
    private readonly TemporaryAudioStore _temporaryStore;
    private readonly AudioProbe _audioProbe;
    private readonly ILogger<TtsResponseValidator> _logger;

    public TtsResponseValidator(
        TemporaryAudioStore temporaryStore,
        AudioProbe audioProbe,
        ILogger<TtsResponseValidator>? logger = null)
    {
        _temporaryStore = temporaryStore;
        _audioProbe = audioProbe;
        _logger = logger ?? NullLogger<TtsResponseValidator>.Instance;
    }

    public async Task<TtsHttpExecutionResult> ValidateAsync(
        ParsedTtsRequest request,
        TtsTransportResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ValidateCoreAsync(request, response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SensitiveFailureLogger.LogError(
                _logger,
                "HTTP TTS response validation",
                exception,
                TtsRequestKnownSecrets.Enumerate(request));
            return Failure(
                TtsErrorKind.Unknown,
                "HTTP TTS 执行失败，请稍后重试。",
                response,
                null);
        }
    }

    private async Task<TtsHttpExecutionResult> ValidateCoreAsync(
        ParsedTtsRequest request,
        TtsTransportResponse response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is 401 or 403)
        {
            return await ErrorAsync(TtsErrorKind.Unauthorized, "服务拒绝了当前请求，请检查鉴权 Header。", response, cancellationToken);
        }

        if (response.StatusCode == 429)
        {
            return await ErrorAsync(TtsErrorKind.RateLimited, "请求过于频繁，服务暂时限流。", response, cancellationToken, response.RetryAfter);
        }

        if (response.StatusCode >= 500)
        {
            return await ErrorAsync(TtsErrorKind.ServerError, "TTS 服务暂时不可用，请稍后重试。", response, cancellationToken);
        }

        if (response.StatusCode is < 200 or >= 300)
        {
            return await ErrorAsync(TtsErrorKind.InvalidResponse, "服务返回了非预期响应。", response, cancellationToken);
        }

        string? temporaryPath = null;
        try
        {
            temporaryPath = await _temporaryStore.WriteAsync(request.RuleId, response.Content, cancellationToken).ConfigureAwait(false);
            if (new FileInfo(temporaryPath).Length == 0)
            {
                return Failure(TtsErrorKind.InvalidResponse, "服务返回了空响应，无法生成音频。", response, null);
            }

            var header = await ReadHeaderAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            var detected = DetectFormat(header);
            if (detected is null && IsText(response.ContentType, header))
            {
                return Failure(TtsErrorKind.InvalidResponse, "服务返回了文本或 JSON，而不是音频。", response, await ReadFileSummaryAsync(temporaryPath, cancellationToken));
            }

            foreach (var extension in CandidateExtensions(detected, response.ContentType, request.DeclaredContentType))
            {
                var candidate = _temporaryStore.CreateCandidate(temporaryPath, extension);
                if (_audioProbe.CanDecode(candidate))
                {
                    TemporaryAudioStore.Delete(temporaryPath);
                    temporaryPath = null;
                    return new TtsHttpExecutionResult(
                        new TtsAudioResponse(candidate, response.StatusCode, response.ContentType, extension), null);
                }

                TemporaryAudioStore.Delete(candidate);
            }

            return Failure(TtsErrorKind.AudioDecode, "下载结果无法被识别为可播放音频。", response, await ReadFileSummaryAsync(temporaryPath, cancellationToken));
        }
        finally
        {
            TemporaryAudioStore.Delete(temporaryPath);
        }
    }

    private static async Task<TtsHttpExecutionResult> ErrorAsync(
        TtsErrorKind kind, string message, TtsTransportResponse response,
        CancellationToken cancellationToken, TimeSpan? retryAfter = null) =>
        new(null, new TtsExecutionFailure(kind, message, response.StatusCode,
            await ReadSummaryAsync(response.Content, cancellationToken), response.ContentType, retryAfter));

    private static TtsHttpExecutionResult Failure(
        TtsErrorKind kind, string message, TtsTransportResponse response, string? summary) =>
        new(null, new TtsExecutionFailure(kind, message, response.StatusCode, summary, response.ContentType, null));

    private static async Task<byte[]> ReadHeaderAsync(string path, CancellationToken token)
    {
        var buffer = new byte[32];
        await using var stream = File.OpenRead(path);
        var count = await stream.ReadAsync(buffer, token);
        return buffer[..count];
    }

    private static async Task<string?> ReadFileSummaryAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return await ReadSummaryAsync(stream, token);
    }

    private static async Task<string?> ReadSummaryAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[MaxSummaryBytes];
        var count = await stream.ReadAsync(buffer, token);
        return count == 0 ? null : SensitiveDataRedactor.RedactJsonLikeText(Encoding.UTF8.GetString(buffer, 0, count));
    }

    private static bool IsText(string? contentType, byte[] header)
    {
        if (contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true ||
            contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
            contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var text = Encoding.UTF8.GetString(header).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        return text.StartsWith('{') || text.StartsWith('[') || text.StartsWith('<');
    }

    private static string? DetectFormat(byte[] value)
    {
        if (value.Length >= 12 && value[0] == 'R' && value[1] == 'I' && value[2] == 'F' && value[3] == 'F' &&
            value[8] == 'W' && value[9] == 'A' && value[10] == 'V' && value[11] == 'E')
        {
            return "wav";
        }

        if (value.Length >= 3 && value[0] == 'I' && value[1] == 'D' && value[2] == '3')
        {
            return "mp3";
        }

        return value.Length >= 2 && value[0] == 0xFF && (value[1] & 0xE0) == 0xE0 ? "mp3" : null;
    }

    private static IEnumerable<string> CandidateExtensions(params string?[] values)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            var extension = value?.Contains("wav", StringComparison.OrdinalIgnoreCase) == true ? "wav" :
                value?.Contains("mpeg", StringComparison.OrdinalIgnoreCase) == true || value?.Contains("mp3", StringComparison.OrdinalIgnoreCase) == true ? "mp3" : value;
            if (extension is "wav" or "mp3" && !result.Contains(extension))
            {
                result.Add(extension);
            }
        }

        if (!result.Contains("mp3"))
        {
            result.Add("mp3");
        }

        if (!result.Contains("wav"))
        {
            result.Add("wav");
        }

        return result;
    }
}
