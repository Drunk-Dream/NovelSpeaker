using System.Diagnostics;
using NovelSpeaker.App.Shared.Feedback;

namespace NovelSpeaker.App.Shell.Activation;

/// <summary>
/// Bridges WPF event handlers to awaitable page operations and safe exception projection.
/// </summary>
public sealed class PageEventOperationRunner
{
    private readonly Action<string, Exception> _reportFailure;

    internal static PageEventOperationRunner DesignTime { get; } = new(
        static (title, exception) => Trace.TraceError(
            "{0}: unhandled design-time page event ({1}).",
            title,
            exception.GetType().Name));

    public PageEventOperationRunner(IAppFeedbackService feedbackService)
        : this((title, exception) => feedbackService.ShowProjectedNotification(
            title,
            feedbackService.Project(exception)))
    {
        ArgumentNullException.ThrowIfNull(feedbackService);
    }

    private PageEventOperationRunner(Action<string, Exception> reportFailure)
    {
        _reportFailure = reportFailure;
    }

    public async Task RunAsync(
        PageActivationController activationController,
        string failureTitle,
        Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(activationController);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureTitle);
        ArgumentNullException.ThrowIfNull(operation);

        var activation = activationController.Current;
        if (activation is null)
        {
            return;
        }

        try
        {
            await operation(activation.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is normal for page and operation replacement lifetimes.
        }
        catch (Exception exception)
        {
            if (activation.IsCurrent)
            {
                _reportFailure(failureTitle, exception);
            }
        }
    }
}
