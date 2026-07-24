namespace NovelSpeaker.App.Shared.Presentation.Platform;

/// <summary>
/// Schedules presentation state updates on the UI thread.
/// </summary>
public interface IUiScheduler
{
    bool CheckAccess();

    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);

    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
