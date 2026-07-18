namespace NovelSpeaker.Application.Speech.Execution;

public sealed class TtsTransportResponse : IAsyncDisposable
{
    private readonly IAsyncDisposable? _owner;

    public TtsTransportResponse(
        int statusCode,
        string? contentType,
        Stream content,
        TimeSpan? retryAfter = null,
        IAsyncDisposable? owner = null)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        Content = content;
        RetryAfter = retryAfter;
        _owner = owner;
    }

    public int StatusCode { get; }
    public string? ContentType { get; }
    public Stream Content { get; }
    public TimeSpan? RetryAfter { get; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Content.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (_owner is not null)
            {
                await _owner.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
