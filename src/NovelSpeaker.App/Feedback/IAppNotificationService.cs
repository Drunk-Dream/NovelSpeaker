namespace NovelSpeaker.App.Feedback;

public interface IAppNotificationService
{
    void ShowSuccess(string title, string message);

    void ShowWarning(string title, string message);

    void ShowError(string title, string message);
}
