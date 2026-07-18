using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>Owns the process-scoped HttpClient and maps compiled requests to HTTP messages.</summary>
public sealed class HttpTtsClient : ITtsHttpTransport, IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;
    private readonly ILogger<HttpTtsClient> _logger;
    private bool _disposed;

    public HttpTtsClient(
        TimeProvider? timeProvider = null,
        TimeSpan? requestTimeout = null,
        ILogger<HttpTtsClient>? logger = null)
        : this(CreateHandler(), timeProvider, requestTimeout, logger)
    {
    }

    internal HttpTtsClient(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null,
        TimeSpan? requestTimeout = null,
        ILogger<HttpTtsClient>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        _logger = logger ?? NullLogger<HttpTtsClient>.Instance;
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<TtsTransportResult> SendAsync(
        ParsedTtsRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_requestTimeout);
        HttpResponseMessage? response = null;
        try
        {
            using var message = CreateRequestMessage(request);
            response = await _client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = new TtsTransportResult(
                new TtsTransportResponse(
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(),
                    stream,
                    ParseRetryAfter(response.Headers.RetryAfter),
                    new ResponseOwner(response)),
                null);
            response = null;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new TtsTransportResult(null, TtsTransportFailureKind.Timeout);
        }
        catch (HttpRequestException exception)
        {
            LogFailure(request, exception, "HTTP TTS network request");
            return new TtsTransportResult(null, TtsTransportFailureKind.Network);
        }
        catch (Exception exception)
        {
            LogFailure(request, exception, "HTTP TTS transport");
            return new TtsTransportResult(null, TtsTransportFailureKind.Unknown);
        }
        finally
        {
            response?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    private static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    };

    private void LogFailure(ParsedTtsRequest request, Exception exception, string operation) =>
        SensitiveFailureLogger.LogError(_logger, operation, exception, TtsRequestKnownSecrets.Enumerate(request));

    private static HttpRequestMessage CreateRequestMessage(ParsedTtsRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        HttpContent? content = request.Body.Kind switch
        {
            ParsedTtsRequestBodyKind.None => null,
            ParsedTtsRequestBodyKind.Json => new StringContent(request.Body.RawText ?? string.Empty, System.Text.Encoding.UTF8, "application/json"),
            ParsedTtsRequestBodyKind.FormUrlEncoded => new FormUrlEncodedContent(request.Body.FormFields ?? new Dictionary<string, string>()),
            _ => new StringContent(request.Body.RawText ?? string.Empty, System.Text.Encoding.UTF8)
        };
        foreach (var header in request.Headers)
        {
            if (content is not null && header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
            }
            else if (content is null || !content.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        message.Content = content;
        return message;
    }

    private TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        var value = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - _timeProvider.GetUtcNow() : null);
        if (value is null)
        {
            return null;
        }

        if (value <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value <= MaxRetryAfter ? value : MaxRetryAfter;
    }

    private sealed class ResponseOwner(HttpResponseMessage response) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            response.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
