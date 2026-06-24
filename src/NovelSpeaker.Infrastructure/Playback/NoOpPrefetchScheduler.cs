using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Placeholder prefetch scheduler used until the dedicated prefetch epic is implemented.
/// </summary>
public sealed class NoOpPrefetchScheduler : IPrefetchScheduler
{
    public Task ScheduleAsync(
        Guid sessionId,
        IReadOnlyList<PlaybackAudioRequest> requests,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
