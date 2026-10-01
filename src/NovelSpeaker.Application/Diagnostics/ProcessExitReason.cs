namespace NovelSpeaker.Application.Diagnostics;

/// <summary>
/// The stable reason for an explicitly reported process end, independent of shutdown mechanics.
/// </summary>
public sealed record ProcessExitReason
{
    private ProcessExitReason(string value) => Value = value;

    public string Value { get; }

    public static ProcessExitReason Normal { get; } = new("normal-exit");
    public static ProcessExitReason StartupFailure { get; } = new("startup-failure");
    public static ProcessExitReason FatalUiFailure { get; } = new("fatal-ui-failure");
    public static ProcessExitReason FatalRuntimeFailure { get; } = new("fatal-runtime-failure");
}
