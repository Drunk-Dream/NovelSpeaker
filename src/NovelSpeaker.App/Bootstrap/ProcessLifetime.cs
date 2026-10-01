using NovelSpeaker.Application.Diagnostics;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Owns the first fatal exit reason. Completing shutdown never resets that reason.
/// </summary>
internal sealed class ProcessLifetime
{
    private ProcessExitReason _exitReason = ProcessExitReason.Normal;

    public ProcessExitReason ExitReason => Volatile.Read(ref _exitReason);

    public void ReportFailure(ProcessFailure failure)
    {
        if (failure.ExitReason is { } reason)
        {
            Interlocked.CompareExchange(ref _exitReason, reason, ProcessExitReason.Normal);
        }
    }
}
