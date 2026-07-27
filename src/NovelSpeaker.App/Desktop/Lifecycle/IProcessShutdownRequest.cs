namespace NovelSpeaker.App.Desktop.Lifecycle;

public interface IProcessShutdownRequest
{
    void Configure(Func<CancellationToken, Task> shutdownAsync);

    Task ShutdownAsync(CancellationToken cancellationToken);
}
