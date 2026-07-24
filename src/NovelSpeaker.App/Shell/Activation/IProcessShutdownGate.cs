namespace NovelSpeaker.App.Shell.Activation;

/// <summary>
/// Blocks new shell submissions while a process shutdown request is being confirmed.
/// </summary>
public interface IProcessShutdownGate
{
    bool IsShutdownRequested { get; }

    CancellationToken ShutdownToken { get; }

    bool TryBeginShutdown();

    void CancelShutdownRequest();
}
