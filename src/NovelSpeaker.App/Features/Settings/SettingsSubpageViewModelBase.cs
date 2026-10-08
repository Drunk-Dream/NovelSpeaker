using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Settings;

/// <summary>
/// Provides activation-scoped behavior for transient settings pages without owning settings state.
/// </summary>
public abstract partial class SettingsSubpageViewModelBase : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private readonly IAppFeedbackService _feedbackService;
    private PageActivationScope? _activation;
    private CancellationToken _activationToken = new(canceled: true);

    protected SettingsSubpageViewModelBase(
        IAppNavigator navigator,
        IAppFeedbackService feedbackService)
    {
        _navigator = navigator;
        _feedbackService = feedbackService;
    }

    protected IAppNavigator Navigator => _navigator;

    protected CancellationToken ActivationToken => _activationToken;

    private protected PageActivationScope? Activation => _activation;

    protected bool IsCurrentActivation(CancellationToken cancellationToken) =>
        _activationToken == cancellationToken &&
        !cancellationToken.IsCancellationRequested && _activation is { IsCurrent: true };

    public void Activate(PageActivationScope activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        if (!ReferenceEquals(_activation, activation)) _activation?.Dispose();
        _activation = activation;
        _activationToken = activation.CancellationToken;
        activation.Register(() =>
        {
            if (ReferenceEquals(_activation, activation)) Deactivate();
        });
    }

    protected void Activate(CancellationToken cancellationToken)
    {
        if (_activation?.CancellationToken == cancellationToken)
        {
            return;
        }

        var activation = new PageActivationController().Activate();
        Activate(activation);
        activation.Register(cancellationToken.Register(activation.Dispose));
    }

    public virtual void Deactivate()
    {
        var activation = _activation;
        _activation = null;
        _activationToken = new CancellationToken(canceled: true);
        activation?.Dispose();
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        await _navigator.NavigateBackAsync(cancellationToken).ConfigureAwait(true);
    }

    protected void ShowSaveFailure(string title, Exception exception)
    {
        var projected = _feedbackService.Project(exception);
        _feedbackService.ShowProjectedNotification(title, projected);
    }

    protected void ShowSuccess(string title, string message)
    {
        _feedbackService.ShowSuccess(title, message);
    }

    protected void RunPageOperation(
        string failureTitle,
        Func<CancellationToken, Task> operation)
    {
        var activation = _activation;
        activation?.Run(
            operation,
            exception => ShowSaveFailure(failureTitle, exception));
    }

    private protected Task RunLatestSaveAsync(
        LatestOperationSlot slot,
        string failureTitle,
        Func<LatestOperationSlot.Operation, Task> save,
        CancellationToken cancellationToken = default)
    {
        if (_activation is not { IsCurrent: true } activation) return Task.CompletedTask;
        var operation = slot.Begin(cancellationToken, activation);
        var task = operation.RunAsync(save, exception => ShowSaveFailure(failureTitle, exception));
        activation.Register(task);
        return task;
    }

    private protected void ScheduleLatestSave(
        LatestOperationSlot slot,
        string failureTitle,
        Func<LatestOperationSlot.Operation, Task> save)
    {
        if (_activation is not { IsCurrent: true } activation) return;
        var operation = slot.Begin(activation: activation);
        activation.Register(operation.RunAsync(save, exception => ShowSaveFailure(failureTitle, exception)));
    }

    public virtual Task LoadAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
