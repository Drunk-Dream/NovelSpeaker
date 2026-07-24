using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Navigation;

namespace NovelSpeaker.App.ViewModels;

public abstract partial class SettingsSubpageViewModelBase : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private readonly IAppFeedbackService _feedbackService;

    protected SettingsSubpageViewModelBase(
        IAppNavigator navigator,
        IAppFeedbackService feedbackService)
    {
        _navigator = navigator;
        _feedbackService = feedbackService;
    }

    protected IAppNavigator Navigator => _navigator;

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
