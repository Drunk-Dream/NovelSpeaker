namespace NovelSpeaker.App.Desktop.Lifecycle;

public interface IDesktopExitGuard
{
    Task<bool> ConfirmExitAsync(CancellationToken cancellationToken);
}
