using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Settings;

public abstract partial class SettingsSubpageViewModelBase : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private readonly IAppFeedbackService _feedbackService;
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

    public void Activate(CancellationToken cancellationToken)
    {
        _activationToken = cancellationToken;
    }

    public void Deactivate()
    {
        _activationToken = new CancellationToken(canceled: true);
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await _navigator.GoBackAsync(cancellationToken).ConfigureAwait(true))
        {
            await _navigator.NavigateAsync(AppRoutes.Settings, cancellationToken).ConfigureAwait(true);
        }
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

    public virtual Task LoadAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
