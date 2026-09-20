namespace NovelSpeaker.App.Features.Diagnostics;

public interface IDiagnosticToolLauncher
{
    Task RecoverAsync(CancellationToken cancellationToken);

    void Open();

    void OpenIfSessionActive();
}
