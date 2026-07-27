namespace NovelSpeaker.App.Desktop.Lifecycle;

internal sealed class ProcessShutdownRequest : IProcessShutdownRequest
{
    private Func<CancellationToken, Task>? _shutdownAsync;

    public void Configure(Func<CancellationToken, Task> shutdownAsync)
    {
        _shutdownAsync = shutdownAsync ?? throw new ArgumentNullException(nameof(shutdownAsync));
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        var shutdownAsync = Volatile.Read(ref _shutdownAsync)
            ?? throw new InvalidOperationException("应用关闭回调尚未配置。");
        return shutdownAsync(cancellationToken);
    }
}
