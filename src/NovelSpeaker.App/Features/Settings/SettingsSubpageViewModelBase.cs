using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Settings;

public abstract partial class SettingsSubpageViewModelBase : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly OwnedTaskRegistry _detachedTasks = new();
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

    protected bool IsCurrentActivation(CancellationToken cancellationToken) =>
        _activationToken == cancellationToken &&
        !cancellationToken.IsCancellationRequested;

    public void Activate(PageActivationScope activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        _activation = activation;
        _activationToken = activation.CancellationToken;
    }

    protected void Activate(CancellationToken cancellationToken)
    {
        if (_activation?.CancellationToken == cancellationToken)
        {
            return;
        }

        _activation = null;
        _activationToken = cancellationToken;
    }

    public virtual void Deactivate()
    {
        _activation = null;
        _activationToken = new CancellationToken(canceled: true);
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
        if (activation is null)
        {
            try
            {
                _detachedTasks.Register(
                    operation(ActivationToken),
                    exception => ShowSaveFailure(failureTitle, exception));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                ShowSaveFailure(failureTitle, exception);
            }

            return;
        }

        activation.Run(
            operation,
            exception => ShowSaveFailure(failureTitle, exception));
    }

    public virtual Task LoadAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
