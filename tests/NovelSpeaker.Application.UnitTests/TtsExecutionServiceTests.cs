using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class TtsExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_rejects_cookie_at_execution_boundary_without_transport()
    {
        var transport = new QueueTransport();
        var service = new TtsExecutionService(transport, new NeverRetryPolicy(), new StubValidator());
        var request = CreateRequest() with
        {
            Headers = new Dictionary<string, string> { ["Cookie"] = "session=secret" }
        };

        var result = await service.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(TtsErrorKind.InvalidRule, result.Failure!.Kind);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_applies_retry_policy_before_response_validation()
    {
        var transport = new QueueTransport(
            Response(500),
            Response(500),
            Response(200));
        var validator = new StubValidator();
        var service = new TtsExecutionService(transport, new TwoServerRetriesPolicy(), validator);

        var result = await service.ExecuteAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, transport.CallCount);
        Assert.Equal(1, validator.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_maps_user_cancellation_without_invoking_validator()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var validator = new StubValidator();
        var service = new TtsExecutionService(new QueueTransport(), new NeverRetryPolicy(), validator);

        var result = await service.ExecuteAsync(CreateRequest(), cancellation.Token);

        Assert.Equal(TtsErrorKind.Cancelled, result.Failure!.Kind);
        Assert.Equal(0, validator.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_maps_validator_caller_cancellation_to_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new TtsExecutionService(
            new QueueTransport(Response(200)),
            new NeverRetryPolicy(),
            new ThrowingValidator(() =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }));

        var result = await service.ExecuteAsync(CreateRequest(), cancellation.Token);

        Assert.Equal(TtsErrorKind.Cancelled, result.Failure!.Kind);
    }

    [Fact]
    public async Task ExecuteAsync_projects_unexpected_validator_exception_as_safe_unknown()
    {
        var service = new TtsExecutionService(
            new QueueTransport(Response(200)),
            new NeverRetryPolicy(),
            new ThrowingValidator(() => throw new IOException("secret response path")));

        var result = await service.ExecuteAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(TtsErrorKind.Unknown, result.Failure!.Kind);
        Assert.DoesNotContain("secret", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_response_disposes_owner_even_when_content_disposal_throws()
    {
        var owner = new TrackingOwner();
        var response = new TtsTransportResponse(
            200,
            "audio/wav",
            new ThrowingDisposeStream(),
            owner: owner);

        await Assert.ThrowsAsync<IOException>(() => response.DisposeAsync().AsTask());

        Assert.True(owner.IsDisposed);
    }

    private static ParsedTtsRequest CreateRequest() => new(
        1,
        "GET",
        new Uri("https://example.com/tts"),
        new Dictionary<string, string>(),
        ParsedTtsRequestBody.None,
        "audio/wav");

    private static TtsTransportResult Response(int statusCode) => new(
        new TtsTransportResponse(statusCode, "audio/wav", new MemoryStream([1])),
        null);

    private sealed class QueueTransport(params TtsTransportResult[] results) : ITtsHttpTransport
    {
        private readonly Queue<TtsTransportResult> _results = new(results);
        public int CallCount { get; private set; }

        public Task<TtsTransportResult> SendAsync(ParsedTtsRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class NeverRetryPolicy : ITtsRetryPolicy
    {
        public bool ShouldRetry(int completedRetries, TtsTransportFailureKind? transportFailure, int? statusCode) => false;
    }

    private sealed class TwoServerRetriesPolicy : ITtsRetryPolicy
    {
        public bool ShouldRetry(int completedRetries, TtsTransportFailureKind? transportFailure, int? statusCode) =>
            completedRetries < 2 && statusCode >= 500;
    }

    private sealed class StubValidator : ITtsResponseValidator
    {
        public int CallCount { get; private set; }

        public Task<TtsHttpExecutionResult> ValidateAsync(
            ParsedTtsRequest request,
            TtsTransportResponse response,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new TtsHttpExecutionResult(
                new TtsAudioResponse("audio.wav", response.StatusCode, response.ContentType, "wav"),
                null));
        }
    }

    private sealed class ThrowingValidator(Action action) : ITtsResponseValidator
    {
        public Task<TtsHttpExecutionResult> ValidateAsync(
            ParsedTtsRequest request,
            TtsTransportResponse response,
            CancellationToken cancellationToken)
        {
            action();
            throw new InvalidOperationException("The configured action did not throw.");
        }
    }

    private sealed class TrackingOwner : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingDisposeStream : MemoryStream
    {
        public override ValueTask DisposeAsync()
        {
            base.Dispose();
            return ValueTask.FromException(new IOException("dispose failed"));
        }
    }
}
