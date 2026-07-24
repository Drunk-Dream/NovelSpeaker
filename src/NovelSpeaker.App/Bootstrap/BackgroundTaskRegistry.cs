namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Owns process-level background tasks, their cancellation observation, and bounded shutdown wait.
/// </summary>
internal sealed class BackgroundTaskRegistry
{
    private readonly object _syncRoot = new();
    private readonly IProcessLifecycleDiagnostics _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly List<Task> _tasks = [];
    private bool _accepting = true;

    public BackgroundTaskRegistry(
        IProcessLifecycleDiagnostics diagnostics,
        TimeProvider timeProvider)
    {
        _diagnostics = diagnostics;
        _timeProvider = timeProvider;
    }

    public void Register(
        string name,
        Func<CancellationToken, Task> operation,
        CancellationToken processToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(operation);

        lock (_syncRoot)
        {
            if (!_accepting)
            {
                throw new InvalidOperationException("进程正在关闭，不能登记新的后台任务。");
            }

            _tasks.Add(Task.Run(
                () => RunObservedAsync(name, operation, processToken),
                CancellationToken.None));
        }
    }

    public void StopAccepting()
    {
        lock (_syncRoot)
        {
            _accepting = false;
        }
    }

    public async Task<bool> WaitForCompletionAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Task[] tasks;
        lock (_syncRoot)
        {
            _accepting = false;
            tasks = [.. _tasks];
        }

        if (tasks.Length == 0)
        {
            return true;
        }

        try
        {
            await Task.WhenAll(tasks)
                .WaitAsync(timeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            TryRecord(() => _diagnostics.RecordStage(
                "background-tasks",
                "等待后台任务退出超时，将继续关闭。"));
            return false;
        }
    }

    private async Task RunObservedAsync(
        string name,
        Func<CancellationToken, Task> operation,
        CancellationToken processToken)
    {
        try
        {
            await operation(processToken).ConfigureAwait(false);
            TryRecord(() => _diagnostics.RecordStage(name, "后台任务已完成。"));
        }
        catch (OperationCanceledException) when (processToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TryRecord(() => _diagnostics.RecordFailure(
                name,
                "后台任务执行失败。",
                exception));
        }
    }

    private static void TryRecord(Action record)
    {
        try
        {
            record();
        }
        catch
        {
            // Lifecycle diagnostics are best effort and must never fault an owned task.
        }
    }
}
