namespace NovelSpeaker.Application.Speech.Execution;

/// <summary>
/// Owns one validated local audio file until a consumer transfers or disposes it.
/// </summary>
public sealed class TtsAudioResponse : IAsyncDisposable
{
    private IAsyncDisposable? _owner;

    public TtsAudioResponse(
        string filePath,
        int statusCode,
        string? responseContentType,
        string? detectedAudioFormat,
        IAsyncDisposable? owner = null)
    {
        FilePath = filePath;
        StatusCode = statusCode;
        ResponseContentType = responseContentType;
        DetectedAudioFormat = detectedAudioFormat;
        _owner = owner;
    }

    public string FilePath { get; }

    public int StatusCode { get; }

    public string? ResponseContentType { get; }

    public string? DetectedAudioFormat { get; }

    public ValueTask DisposeAsync()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        return owner?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
