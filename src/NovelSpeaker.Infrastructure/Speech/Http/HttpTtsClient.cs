using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using NAudio.Wave;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>
/// Executes compiled HTTP TTS requests against remote services and validates returned audio files.
/// </summary>
public sealed class HttpTtsClient : IHttpTtsClient, IDisposable
{
    private const int MaxTransientRetries = 2;
    private const int MaxErrorBodyBytes = 4096;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<long, HttpClient> _clients = new();
    private readonly string _tempDirectoryPath;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public HttpTtsClient(IAppDataDirectoryProvider directories, TimeProvider? timeProvider = null)
    {
        _tempDirectoryPath = Path.Combine(directories.CacheDirectoryPath, "RuleTests");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TtsHttpExecutionResult> ExecuteAsync(
        ParsedTtsRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Directory.CreateDirectory(_tempDirectoryPath);

        var transientRetriesRemaining = MaxTransientRetries;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(request.Timeout);

            try
            {
                using var message = CreateRequestMessage(request);
                using var response = await GetClient(request.RuleId).SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return Failure(
                        TtsErrorKind.Unauthorized,
                        "服务拒绝了当前请求，请检查登录信息或鉴权 Header。",
                        (int)response.StatusCode,
                        await ReadErrorSummaryAsync(response, cancellationToken),
                        response.Content.Headers.ContentType?.ToString(),
                        null);
                }

                if ((int)response.StatusCode == 429)
                {
                    var retryAfter = ParseRetryAfter(response.Headers.RetryAfter, _timeProvider);
                    return Failure(
                        TtsErrorKind.RateLimited,
                        "请求过于频繁，服务暂时限流。",
                        (int)response.StatusCode,
                        await ReadErrorSummaryAsync(response, cancellationToken),
                        response.Content.Headers.ContentType?.ToString(),
                        retryAfter);
                }

                if ((int)response.StatusCode >= 500)
                {
                    if (transientRetriesRemaining > 0)
                    {
                        transientRetriesRemaining--;
                        continue;
                    }

                    return Failure(
                        TtsErrorKind.ServerError,
                        "TTS 服务暂时不可用，请稍后重试。",
                        (int)response.StatusCode,
                        await ReadErrorSummaryAsync(response, cancellationToken),
                        response.Content.Headers.ContentType?.ToString(),
                        null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Failure(
                        TtsErrorKind.InvalidResponse,
                        "服务返回了非预期响应。",
                        (int)response.StatusCode,
                        await ReadErrorSummaryAsync(response, cancellationToken),
                        response.Content.Headers.ContentType?.ToString(),
                        null);
                }

                return await DownloadAndValidateAudioAsync(request, response, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (transientRetriesRemaining > 0)
                {
                    transientRetriesRemaining--;
                    continue;
                }

                return Failure(TtsErrorKind.Timeout, "请求超时，请稍后重试。", null, null, null, null);
            }
            catch (OperationCanceledException)
            {
                return Failure(TtsErrorKind.Cancelled, "已取消当前 HTTP TTS 请求。", null, null, null, null);
            }
            catch (HttpRequestException exception)
            {
                if (transientRetriesRemaining > 0)
                {
                    transientRetriesRemaining--;
                    continue;
                }

                return Failure(TtsErrorKind.Network, $"网络请求失败：{exception.Message}", null, null, null, null);
            }
            catch (Exception exception)
            {
                return Failure(TtsErrorKind.Unknown, $"HTTP TTS 执行失败：{exception.Message}", null, null, null, null);
            }
        }
    }

    public Task ClearRuleCookiesAsync(long ruleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_clients.TryRemove(ruleId, out var client))
        {
            client.Dispose();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
    }

    private async Task<TtsHttpExecutionResult> DownloadAndValidateAudioAsync(
        ParsedTtsRequest request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var basePath = Path.Combine(_tempDirectoryPath, $"tts-test-{request.RuleId}-{Guid.NewGuid():N}");
        var tempPath = $"{basePath}.tmp";

        try
        {
            await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = File.Create(tempPath))
            {
                await responseStream.CopyToAsync(fileStream, cancellationToken);
            }

            var fileInfo = new FileInfo(tempPath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                return Failure(
                    TtsErrorKind.InvalidResponse,
                    "服务返回了空响应，无法生成音频。",
                    (int)response.StatusCode,
                    null,
                    response.Content.Headers.ContentType?.ToString(),
                    null,
                    tempPath);
            }

            var headerBytes = await ReadHeaderBytesAsync(tempPath, cancellationToken);
            var detectedFormat = DetectAudioFormat(headerBytes);
            if (detectedFormat is null &&
                IsLikelyTextualPayload(response.Content.Headers.ContentType?.MediaType, headerBytes))
            {
                return Failure(
                    TtsErrorKind.InvalidResponse,
                    "服务返回了文本或 JSON，而不是音频。",
                    (int)response.StatusCode,
                    await ReadFileSummaryAsync(tempPath, cancellationToken),
                    response.Content.Headers.ContentType?.ToString(),
                    null,
                    tempPath);
            }

            var validation = TryValidateAudioFile(
                tempPath,
                detectedFormat,
                response.Content.Headers.ContentType?.MediaType,
                request.DeclaredContentType);
            if (!validation.IsSuccess)
            {
                return Failure(
                    validation.Kind!.Value,
                    validation.Message!,
                    (int)response.StatusCode,
                    await ReadFileSummaryAsync(tempPath, cancellationToken),
                    response.Content.Headers.ContentType?.ToString(),
                    null,
                    tempPath);
            }

            TryDeleteFile(tempPath);
            return new TtsHttpExecutionResult(
                new TtsAudioResponse(
                    validation.FilePath!,
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(),
                    validation.DetectedFormat),
                null);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private HttpClient GetClient(long ruleId)
    {
        return _clients.GetOrAdd(ruleId, _ =>
        {
            var handler = new SocketsHttpHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            return new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        });
    }

    private static HttpRequestMessage CreateRequestMessage(ParsedTtsRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        HttpContent? content = null;

        if (request.Body.Kind != ParsedTtsRequestBodyKind.None)
        {
            content = request.Body.Kind switch
            {
                ParsedTtsRequestBodyKind.Json => new StringContent(
                    request.Body.RawText ?? string.Empty,
                    System.Text.Encoding.UTF8,
                    "application/json"),
                ParsedTtsRequestBodyKind.FormUrlEncoded => new FormUrlEncodedContent(
                    request.Body.FormFields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                _ => new StringContent(request.Body.RawText ?? string.Empty, System.Text.Encoding.UTF8)
            };
        }

        foreach (var header in request.Headers)
        {
            if (content is not null && header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
                continue;
            }

            if (content is not null && content.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                continue;
            }

            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        message.Content = content;
        return message;
    }

    private static async Task<string?> ReadErrorSummaryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await ReadSummaryFromStreamAsync(stream, cancellationToken);
    }

    private static async Task<byte[]> ReadHeaderBytesAsync(string filePath, CancellationToken cancellationToken)
    {
        var buffer = new byte[32];
        await using var stream = File.OpenRead(filePath);
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        return buffer[..bytesRead];
    }

    private static async Task<string?> ReadFileSummaryAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        return await ReadSummaryFromStreamAsync(stream, cancellationToken);
    }

    private static async Task<string?> ReadSummaryFromStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxErrorBodyBytes];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        if (bytesRead <= 0)
        {
            return null;
        }

        var summary = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
        return SensitiveDataRedactor.RedactJsonLikeText(summary);
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? retryAfter, TimeProvider timeProvider)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta <= MaxRetryAfter ? delta : MaxRetryAfter;
        }

        if (retryAfter?.Date is { } date)
        {
            var remaining = date - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return remaining <= MaxRetryAfter ? remaining : MaxRetryAfter;
        }

        return null;
    }

    private static bool IsLikelyTextualPayload(string? mediaType, byte[] headerBytes)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
             mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
             mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var text = System.Text.Encoding.UTF8.GetString(headerBytes).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        return text.StartsWith('{') || text.StartsWith('[') || text.StartsWith('<');
    }

    private static string? DetectAudioFormat(byte[] headerBytes)
    {
        if (headerBytes.Length >= 12 &&
            headerBytes[0] == (byte)'R' &&
            headerBytes[1] == (byte)'I' &&
            headerBytes[2] == (byte)'F' &&
            headerBytes[3] == (byte)'F' &&
            headerBytes[8] == (byte)'W' &&
            headerBytes[9] == (byte)'A' &&
            headerBytes[10] == (byte)'V' &&
            headerBytes[11] == (byte)'E')
        {
            return "wav";
        }

        if (headerBytes.Length >= 3 &&
            headerBytes[0] == (byte)'I' &&
            headerBytes[1] == (byte)'D' &&
            headerBytes[2] == (byte)'3')
        {
            return "mp3";
        }

        if (headerBytes.Length >= 2 &&
            headerBytes[0] == 0xFF &&
            (headerBytes[1] & 0xE0) == 0xE0)
        {
            return "mp3";
        }

        return null;
    }

    private static AudioValidationResult TryValidateAudioFile(
        string tempPath,
        string? detectedFormat,
        string? responseContentType,
        string? declaredContentType)
    {
        var candidates = new List<string>();
        AddCandidate(candidates, detectedFormat);
        AddCandidate(candidates, MapContentTypeToExtension(responseContentType));
        AddCandidate(candidates, MapContentTypeToExtension(declaredContentType));
        AddCandidate(candidates, "mp3");
        AddCandidate(candidates, "wav");

        foreach (var extension in candidates)
        {
            var candidatePath = Path.ChangeExtension(tempPath, extension);
            TryDeleteFile(candidatePath);
            File.Copy(tempPath, candidatePath, overwrite: true);

            try
            {
                using var reader = new AudioFileReader(candidatePath);
                if (reader.TotalTime <= TimeSpan.Zero)
                {
                    TryDeleteFile(candidatePath);
                    continue;
                }

                return AudioValidationResult.Success(candidatePath, extension);
            }
            catch
            {
                TryDeleteFile(candidatePath);
            }
        }

        return AudioValidationResult.Error(TtsErrorKind.AudioDecode, "下载结果无法被识别为可播放音频。");
    }

    private static void AddCandidate(List<string> candidates, string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension) ||
            candidates.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        candidates.Add(extension);
    }

    private static string? MapContentTypeToExtension(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        if (contentType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("mp3", StringComparison.OrdinalIgnoreCase))
        {
            return "mp3";
        }

        if (contentType.Contains("wav", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("wave", StringComparison.OrdinalIgnoreCase))
        {
            return "wav";
        }

        return null;
    }

    private static TtsHttpExecutionResult Failure(
        TtsErrorKind kind,
        string message,
        int? statusCode,
        string? responseSummary,
        string? responseContentType,
        TimeSpan? retryAfter,
        string? tempFilePath = null)
    {
        if (tempFilePath is not null)
        {
            TryDeleteFile(tempFilePath);
        }

        return new TtsHttpExecutionResult(
            null,
            new TtsExecutionFailure(kind, message, statusCode, responseSummary, responseContentType, retryAfter));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record AudioValidationResult(
        bool IsSuccess,
        string? FilePath,
        string? DetectedFormat,
        TtsErrorKind? Kind,
        string? Message)
    {
        public static AudioValidationResult Success(string filePath, string detectedFormat) =>
            new(true, filePath, detectedFormat, null, null);

        public static AudioValidationResult Error(TtsErrorKind kind, string message) =>
            new(false, null, null, kind, message);
    }
}
