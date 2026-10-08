namespace NovelSpeaker.Application.Cache;

/// <summary>Drains Cache-owned display changes during process shutdown.</summary>
public interface ICacheChangeLifetime
{
    Task StopAsync(CancellationToken cancellationToken);
}
