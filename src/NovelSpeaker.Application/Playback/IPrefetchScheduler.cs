namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Schedules best-effort background generation for upcoming segments.
/// </summary>
public interface IPrefetchScheduler
{
    Task ScheduleAsync(
        Guid sessionId,
        IReadOnlyList<PlaybackAudioRequest> requests,
        CancellationToken cancellationToken);

    Task CancelAsync(Guid sessionId, CancellationToken cancellationToken);
}
