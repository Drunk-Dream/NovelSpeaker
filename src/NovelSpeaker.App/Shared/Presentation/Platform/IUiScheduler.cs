namespace NovelSpeaker.App.Shared.Presentation.Platform;

/// <summary>
/// Schedules presentation state updates on the UI thread.
/// </summary>
public interface IUiScheduler
{
    bool CheckAccess();

    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);

    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts work even when the caller already owns the UI thread, allowing a staged
    /// projection to yield between bounded batches. Lightweight test schedulers may
    /// use the default inline implementation.
    /// </summary>
    Task InvokeLaterAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
