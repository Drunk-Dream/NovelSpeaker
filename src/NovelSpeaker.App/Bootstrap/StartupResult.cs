namespace NovelSpeaker.App.Bootstrap;

internal sealed record StartupFailure(
    StartupStage Stage,
    string Title,
    string Message);

internal sealed record StartupResult(
    bool IsSuccessful,
    bool IsCancelled,
    StartupFailure? Failure)
{
    public static StartupResult Successful { get; } = new(true, false, null);

    public static StartupResult Cancelled { get; } = new(false, true, null);

    public static StartupResult Failed(StartupFailure failure) => new(false, false, failure);
}
