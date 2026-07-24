namespace NovelSpeaker.App.Features.Diagnostics;

public interface IAppDiagnosticsService
{
    Task<AppDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<string> GetRedactedSummaryAsync(CancellationToken cancellationToken);

    Task OpenAppDataDirectoryAsync(CancellationToken cancellationToken);

    Task OpenLogsDirectoryAsync(CancellationToken cancellationToken);

    Task OpenThirdPartyNoticesAsync(CancellationToken cancellationToken);
}
