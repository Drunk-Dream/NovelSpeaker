namespace NovelSpeaker.App.Shared.Presentation;

/// <summary>
/// Keeps fire-and-forget work attached to its semantic owner and always observes completion.
/// Cancellation remains the responsibility of the page, operation, session, or process owner.
/// </summary>
internal sealed class OwnedTaskRegistry
{
    private readonly object _syncRoot = new();
    private readonly HashSet<Task> _tasks = [];

    public int PendingCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _tasks.Count;
            }
        }
    }

    public void Register(Task task, Action<Exception>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_syncRoot)
        {
            _tasks.Add(task);
        }

        var awaiter = task.ConfigureAwait(false).GetAwaiter();
        if (awaiter.IsCompleted)
        {
            Complete(task, reportFailure);
            return;
        }

        awaiter.UnsafeOnCompleted(() => Complete(task, reportFailure));
    }

    private void Complete(Task task, Action<Exception>? reportFailure)
    {
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            reportFailure?.Invoke(exception);
        }
        finally
        {
            lock (_syncRoot)
            {
                _tasks.Remove(task);
            }
        }
    }
}
