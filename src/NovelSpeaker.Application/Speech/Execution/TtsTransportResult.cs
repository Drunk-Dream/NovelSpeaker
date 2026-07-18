namespace NovelSpeaker.Application.Speech.Execution;

public enum TtsTransportFailureKind
{
    Timeout,
    Network,
    Unknown
}

public sealed record TtsTransportResult(
    TtsTransportResponse? Response,
    TtsTransportFailureKind? FailureKind)
{
    public bool IsSuccess => Response is not null && FailureKind is null;
}
