using System.Windows.Threading;

namespace NovelSpeaker.App.Shared.Presentation.Platform;

public sealed class WpfUiScheduler : IUiScheduler
{
    private readonly Dispatcher _dispatcher;

    public WpfUiScheduler()
        : this(
            System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
    {
    }

    internal WpfUiScheduler(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return CheckAccess()
            ? InvokeInlineAsync(action)
            : _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;
    }

    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return CheckAccess()
            ? InvokeInlineAsync(action)
            : _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }

    public Task InvokeLaterAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return _dispatcher.InvokeAsync(action, DispatcherPriority.Background, cancellationToken).Task;
    }

    private static Task InvokeInlineAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static async Task InvokeInlineAsync(Func<Task> action)
    {
        await action().ConfigureAwait(false);
    }
}
