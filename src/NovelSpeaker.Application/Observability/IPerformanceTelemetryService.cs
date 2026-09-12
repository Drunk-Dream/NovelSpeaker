namespace NovelSpeaker.Application.Observability;

/// <summary>
/// User-controlled local performance telemetry and diagnostics export boundary.
/// </summary>
public interface IPerformanceTelemetryService
{
    bool IsEnabled { get; }

    void SetCollectionEnabled(bool enabled);

    Task ClearAsync(CancellationToken cancellationToken);

    Task ExportAsync(string destinationPath, CancellationToken cancellationToken);
}
