using System;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shared.Feedback;

public sealed class AppNotificationService : IAppNotificationService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly ISnackbarService _snackbarService;

    public AppNotificationService(ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;
    }

    public void ShowSuccess(string title, string message)
    {
        Show(title, message, ControlAppearance.Primary);
    }

    public void ShowWarning(string title, string message)
    {
        Show(title, message, ControlAppearance.Caution);
    }

    public void ShowError(string title, string message)
    {
        Show(title, message, ControlAppearance.Danger);
    }

    private void Show(string title, string message, ControlAppearance appearance)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => ShowCore(title, message, appearance));
            return;
        }

        ShowCore(title, message, appearance);
    }

    private void ShowCore(string title, string message, ControlAppearance appearance)
    {
        SnackbarPresenter? presenter = null;

        try
        {
            presenter = _snackbarService.GetSnackbarPresenter();
        }
        catch
        {
            presenter = null;
        }

        if (presenter is null)
        {
            return;
        }

        _snackbarService.Show(title, message, appearance, icon: null, DefaultTimeout);
    }
}
