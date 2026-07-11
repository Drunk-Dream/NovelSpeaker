using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public abstract partial class SettingsSubpageViewModelBase : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IAppFeedbackService _feedbackService;

    protected SettingsSubpageViewModelBase(
        INavigationService navigationService,
        IAppFeedbackService feedbackService)
    {
        _navigationService = navigationService;
        _feedbackService = feedbackService;
    }

    protected INavigationService NavigationService => _navigationService;

    [RelayCommand]
    private void Back()
    {
        if (!_navigationService.GoBack())
        {
            _navigationService.NavigateWithHierarchy(typeof(SettingsPage));
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
