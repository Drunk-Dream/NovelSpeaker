using System.Windows.Threading;
using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.App.Shared.Presentation.Platform;

public sealed class WpfUiScheduler : IUiScheduler
{
    private readonly Dispatcher _dispatcher;
    private readonly IObservability _observability;

    public WpfUiScheduler(IObservability? observability = null)
        : this(
            System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher,
            observability)
    {
    }

    internal WpfUiScheduler(Dispatcher dispatcher, IObservability? observability = null)
    {
        _dispatcher = dispatcher;
        _observability = observability ?? new ObservabilityHub(new ObservabilityContextAccessor());
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (CheckAccess())
        {
            return InvokeInlineAsync(action);
        }

        return InvokeOnDispatcherAsync(action, cancellationToken);
    }

    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (CheckAccess())
        {
            return InvokeInlineAsync(action);
        }

        return InvokeOnDispatcherAsync(action, cancellationToken);
    }

    public Task InvokeLaterAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return InvokeOnDispatcherAsync(action, cancellationToken, DispatcherPriority.Background);
    }

    private async Task InvokeOnDispatcherAsync(
        Action action,
        CancellationToken cancellationToken,
        DispatcherPriority priority = DispatcherPriority.Normal)
    {
        using var operation = _observability.StartOperation(OperationCatalog.UiDispatcherStall);
        try
        {
            await _dispatcher.InvokeAsync(action, priority, cancellationToken).Task.ConfigureAwait(false);
            operation.Complete(OperationResult.Succeeded());
        }
        catch (OperationCanceledException)
        {
            operation.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            operation.Complete(OperationResult.Failed("dispatcher-work-failed"));
            throw;
        }
    }

    private Task InvokeInlineAsync(Action action)
    {
        using var operation = _observability.StartOperation(OperationCatalog.UiDispatcherStall);
        try
        {
            action();
            operation.Complete(OperationResult.Succeeded());
            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            operation.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            operation.Complete(OperationResult.Failed("dispatcher-work-failed"));
            throw;
        }
    }

    private async Task InvokeInlineAsync(Func<Task> action)
    {
        using var operation = _observability.StartOperation(OperationCatalog.UiDispatcherStall);
        try
        {
            await action().ConfigureAwait(false);
            operation.Complete(OperationResult.Succeeded());
        }
        catch (OperationCanceledException)
        {
            operation.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            operation.Complete(OperationResult.Failed("dispatcher-work-failed"));
            throw;
        }
    }

    private async Task InvokeOnDispatcherAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        using var operation = _observability.StartOperation(OperationCatalog.UiDispatcherStall);
        try
        {
            await _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task
                .Unwrap()
                .ConfigureAwait(false);
            operation.Complete(OperationResult.Succeeded());
        }
        catch (OperationCanceledException)
        {
            operation.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            operation.Complete(OperationResult.Failed("dispatcher-work-failed"));
            throw;
        }
    }
}
