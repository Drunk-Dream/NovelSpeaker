namespace NovelSpeaker.Application.Diagnostics;

/// <summary>
/// A classified process failure shared by logging, diagnostic events and the lifetime owner.
/// Contains no exception or user content.
/// </summary>
public sealed record ProcessFailure
{
    private ProcessFailure(string source, ProcessExitReason? exitReason)
    {
        Source = source;
        ExitReason = exitReason;
    }

    public string Source { get; }
    public ProcessExitReason? ExitReason { get; }
    public bool IsFatal => ExitReason is not null;
    public string Severity => IsFatal ? "fatal" : "error";
    public string Action => IsFatal ? "exit" : "continue";

    public static ProcessFailure Startup { get; } = new("startup", ProcessExitReason.StartupFailure);
    public static ProcessFailure FatalUi { get; } = new("dispatcher", ProcessExitReason.FatalUiFailure);
    public static ProcessFailure FatalRuntime { get; } = new("runtime", ProcessExitReason.FatalRuntimeFailure);
    public static ProcessFailure ObservedRuntime { get; } = new("runtime", null);
    public static ProcessFailure ObservedTask { get; } = new("task", null);
}
