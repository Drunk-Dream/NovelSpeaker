namespace NovelSpeaker.Application.Cache;

/// <summary>Stops process-owned speech plan repair without exposing its query protocol.</summary>
public interface ISpeechPlanRepairLifetime
{
    Task StopAsync(CancellationToken cancellationToken);
}
