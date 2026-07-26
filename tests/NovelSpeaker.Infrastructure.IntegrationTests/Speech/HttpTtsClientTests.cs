using System.Net;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.UnitTests.Common;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class HttpTtsClientTests
{
    [Fact]
    public async Task ExecuteAsync_downloads_and_validates_audio()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "audio")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Audio);
        Assert.True(File.Exists(result.Audio!.FilePath));
        await result.Audio.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_returns_retry_after_for_rate_limited_requests()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "rate-limited")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.RateLimited, result.Failure!.Kind);
        Assert.Equal(TimeSpan.Zero, result.Failure.RetryAfter);
        Assert.Equal(1, server.GetRequestCount("/rate-limited"));
    }

    [Fact]
    public async Task ExecuteAsync_sends_post_json_and_form_payloads()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var jsonResult = await client.ExecuteAsync(
            new ParsedTtsRequest(
                10,
                "POST",
                new Uri(server.BaseUri, "audio-json"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Accept"] = "*/*",
                    ["User-Agent"] = "NovelSpeaker.Tests",
                    ["Content-Type"] = "application/json"
                },
                new ParsedTtsRequestBody(ParsedTtsRequestBodyKind.Json, """{"text":"demo"}""", null),
                "audio/mpeg"),
            CancellationToken.None);
        var formResult = await client.ExecuteAsync(
            new ParsedTtsRequest(
                11,
                "POST",
                new Uri(server.BaseUri, "audio-form"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Accept"] = "*/*",
                    ["User-Agent"] = "NovelSpeaker.Tests",
                    ["Content-Type"] = "application/x-www-form-urlencoded"
                },
                new ParsedTtsRequestBody(
                    ParsedTtsRequestBodyKind.FormUrlEncoded,
                    "text=demo&speed=10",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["text"] = "demo",
                        ["speed"] = "10"
                    }),
                "audio/wav"),
            CancellationToken.None);

        Assert.True(jsonResult.IsSuccess);
        Assert.True(formResult.IsSuccess);
        Assert.Equal("""{"text":"demo"}""", server.LastJsonBody);
        Assert.NotNull(server.LastFormBody);
        Assert.Contains("text=demo", server.LastFormBody);
        Assert.Contains("speed=10", server.LastFormBody);
        await jsonResult.Audio!.DisposeAsync();
        await formResult.Audio!.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_classifies_json_error_and_redacts_summary()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "error-json")),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidResponse, result.Failure!.Kind);
        Assert.DoesNotContain("super-secret", result.Failure.ResponseSummary);
    }

    [Fact]
    public async Task ExecuteAsync_times_out_when_request_exceeds_timeout()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient(TimeSpan.FromMilliseconds(50));

        var requestTask = client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "slow")),
            CancellationToken.None);
        await server.SlowRequestStarted.WaitAsync(TimeSpan.FromSeconds(5));
        var timeoutResult = await requestTask;

        Assert.False(timeoutResult.IsSuccess);
        Assert.Equal(TtsErrorKind.Timeout, timeoutResult.Failure!.Kind);
    }

    [Fact]
    public async Task ExecuteAsync_retries_transient_server_errors()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var retryResult = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "server-error")),
            CancellationToken.None);

        Assert.True(retryResult.IsSuccess);
        Assert.Equal(3, server.GetRequestCount("/server-error"));
        await retryResult.Audio!.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_rejects_corrupt_audio()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri(server.BaseUri, "corrupt-audio"), declaredContentType: "audio/mpeg"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.AudioDecode, result.Failure!.Kind);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_cookie_header_before_sending_request()
    {
        await using var server = new LocalHttpTtsTestServer();
        using var client = CreateClient();
        var request = CreateRequest(1, new Uri(server.BaseUri, "cookie-required")) with
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cookie"] = "session=rule-cookie"
            }
        };

        var result = await client.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidRule, result.Failure!.Kind);
        Assert.Contains("Cookie/LoginInfo", result.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, server.GetRequestCount("/cookie-required"));
    }

    [Fact]
    public async Task ExecuteAsync_projects_safe_network_failure_and_redacts_diagnostic_log()
    {
        const string token = "fixture-token-3218";
        const string body = "fixture-body-4790";
        const string novelText = "fixture-novel-text-6527";
        var logger = new CapturingLogger<HttpTtsClient>();
        var exceptionText = $"Authorization=Bearer {token}; query={token}; body={body}; text={novelText}";
        using var client = CreateClient(new ThrowingHandler(new HttpRequestException(exceptionText)), logger);
        var request = new ParsedTtsRequest(
            1,
            "POST",
            new Uri($"https://example.com/tts?token={token}"),
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            new ParsedTtsRequestBody(ParsedTtsRequestBodyKind.Json, $"{{\"body\":\"{body}\",\"text\":\"{novelText}\"}}", null),
            "audio/mpeg");

        var result = await client.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.Network, result.Failure!.Kind);
        Assert.Equal("网络请求失败，请检查网络连接后重试。", result.Failure.Message);
        Assert.Equal(3, logger.Entries.Count);
        foreach (var entry in logger.Entries)
        {
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Null(entry.Exception);
            AssertSensitiveValuesAbsent(entry.Message, token, body, novelText);
        }
        AssertSensitiveValuesAbsent(result.Failure.Message, token, body, novelText);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_log_user_cancellation_as_error()
    {
        var logger = new CapturingLogger<HttpTtsClient>();
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(new CancellingHandler(cancellation), logger);

        var result = await client.ExecuteAsync(
            CreateRequest(1, new Uri("https://example.com/tts")),
            cancellation.Token);

        Assert.Equal(TtsErrorKind.Cancelled, result.Failure!.Kind);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task SendAsync_transfers_response_ownership_until_result_is_disposed()
    {
        var content = new TrackingContent([1, 2, 3]);
        using var transport = new HttpTtsClient(new StaticResponseHandler(content));

        var result = await transport.SendAsync(
            CreateRequest(1, new Uri("https://example.com/tts")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(content.IsDisposed);
        await result.Response!.DisposeAsync();
        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_times_out_while_reading_response_body_and_disposes_response()
    {
        var content = new BlockingReadContent();
        using var transport = new HttpTtsClient(
            new StaticResponseHandler(content),
            requestTimeout: TimeSpan.FromMilliseconds(50));
        using var harness = CreateHarness(CreateDirectories(), transport);

        var result = await harness.ExecuteAsync(
                CreateRequest(1, new Uri("https://example.com/tts")),
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TtsErrorKind.Timeout, result.Failure!.Kind);
        Assert.True(content.IsDisposed);
        Assert.True(content.Stream.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_cancellation_while_reading_response_body_disposes_response()
    {
        var content = new BlockingReadContent();
        using var transport = new HttpTtsClient(new StaticResponseHandler(content));
        using var harness = CreateHarness(CreateDirectories(), transport);
        using var cancellation = new CancellationTokenSource();

        var resultTask = harness.ExecuteAsync(
            CreateRequest(1, new Uri("https://example.com/tts")),
            cancellation.Token);
        await content.Stream.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TtsErrorKind.Cancelled, result.Failure!.Kind);
        Assert.True(content.IsDisposed);
        Assert.True(content.Stream.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsync_read_failure_disposes_response()
    {
        var content = new ThrowingReadContent();
        using var transport = new HttpTtsClient(new StaticResponseHandler(content));
        using var harness = CreateHarness(CreateDirectories(), transport);

        var result = await harness.ExecuteAsync(
            CreateRequest(1, new Uri("https://example.com/tts")),
            CancellationToken.None);

        Assert.Equal(TtsErrorKind.Unknown, result.Failure!.Kind);
        Assert.True(content.IsDisposed);
        Assert.True(content.Stream.IsDisposed);
    }

    private static ExecutionHarness CreateClient(TimeSpan? requestTimeout = null)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        directories.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return CreateHarness(directories, new HttpTtsClient(requestTimeout: requestTimeout));
    }

    private static ExecutionHarness CreateClient(
        HttpMessageHandler handler,
        Microsoft.Extensions.Logging.ILogger<HttpTtsClient> logger)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        directories.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return CreateHarness(directories, new HttpTtsClient(handler, logger: logger));
    }

    private static LocalAppDataDirectoryProvider CreateDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        directories.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return directories;
    }

    private static ExecutionHarness CreateHarness(
        LocalAppDataDirectoryProvider directories,
        HttpTtsClient transport)
    {
        var validator = new TtsResponseValidator(new TemporaryAudioStore(directories), new AudioProbe());
        return new ExecutionHarness(
            new TtsExecutionService(transport, new TtsRetryPolicy(), validator),
            transport);
    }

    private sealed class ExecutionHarness(TtsExecutionService service, HttpTtsClient transport) : IDisposable
    {
        public Task<TtsHttpExecutionResult> ExecuteAsync(
            ParsedTtsRequest request,
            CancellationToken cancellationToken) =>
            service.ExecuteAsync(request, cancellationToken);

        public void Dispose()
        {
            transport.Dispose();
        }
    }

    private static void AssertSensitiveValuesAbsent(string value, params string[] sensitiveValues)
    {
        foreach (var sensitiveValue in sensitiveValues)
        {
            Assert.DoesNotContain(sensitiveValue, value, StringComparison.Ordinal);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CancellingHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        }
    }

    private sealed class StaticResponseHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class TrackingContent(byte[] bytes) : HttpContent
    {
        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = bytes.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingReadContent : HttpContent
    {
        public BlockingReadStream Stream { get; } = new();

        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(Stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingReadContent : HttpContent
    {
        public ThrowingReadStream Stream { get; } = new();

        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(Stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public bool IsDisposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingReadStream : Stream
    {
        public bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("fixture read failure");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("fixture read failure"));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private static ParsedTtsRequest CreateRequest(
        long ruleId,
        Uri url,
        string method = "GET",
        string? declaredContentType = "audio/wav")
    {
        return new ParsedTtsRequest(
            ruleId,
            method,
            url,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Accept"] = "*/*",
                ["User-Agent"] = "NovelSpeaker.Tests"
            },
            ParsedTtsRequestBody.None,
            declaredContentType);
    }
}
