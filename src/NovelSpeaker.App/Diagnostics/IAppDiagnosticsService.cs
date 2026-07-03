namespace NovelSpeaker.App.Diagnostics;

public interface IAppDiagnosticsService
{
    Task<AppDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task OpenLogsDirectoryAsync(CancellationToken cancellationToken);
}
