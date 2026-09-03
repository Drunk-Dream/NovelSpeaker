namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes the inputs needed to prepare and play one current segment.
/// </summary>
internal sealed record PlaybackSegmentRunRequest(
    PlaybackAudioRequest AudioRequest,
    string DisplayTitle,
    long ResumePositionMilliseconds,
    bool ForceInvalidate);

/// <summary>
/// Captures the result of one segment execution without owning playback state or publishing events.
/// </summary>
internal sealed record PlaybackSegmentRunResult(
    PlaybackAudioResult Audio,
    LocalAudioPlaybackSnapshot LocalSnapshot);

/// <summary>
/// Obtains one segment's audio and hands it to the local audio coordinator.
/// Long-lived session state and UI projection remain owned by <see cref="PlaybackCoordinator"/>.
/// </summary>
internal sealed class PlaybackSegmentRunner
{
    private readonly IPlaybackAudioProvider _audioProvider;
    private readonly ILocalAudioPlaybackCoordinator _localAudioPlaybackCoordinator;

    public PlaybackSegmentRunner(
        IPlaybackAudioProvider audioProvider,
        ILocalAudioPlaybackCoordinator localAudioPlaybackCoordinator)
    {
        _audioProvider = audioProvider;
        _localAudioPlaybackCoordinator = localAudioPlaybackCoordinator;
    }

    public async Task<PlaybackSegmentRunResult> RunAsync(
        PlaybackSegmentRunRequest request,
        Action<PlaybackAudioProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ForceInvalidate)
        {
            await _audioProvider.InvalidateAsync(
                request.AudioRequest,
                cancellationToken).ConfigureAwait(false);
        }

        var audio = await _audioProvider.GetAudioAsync(
            request.AudioRequest,
            PlaybackAudioPriority.Current,
            progressCallback,
            cancellationToken).ConfigureAwait(false);
        if (!audio.IsSuccess)
        {
            return new PlaybackSegmentRunResult(audio, _localAudioPlaybackCoordinator.CurrentSnapshot);
        }

        await _localAudioPlaybackCoordinator.StartAsync(
            new LocalAudioPlaybackRequest(
                audio.FilePath!,
                request.DisplayTitle,
                request.AudioRequest.BookId,
                request.AudioRequest.ChapterIndex,
                request.AudioRequest.SegmentIndex,
                request.ResumePositionMilliseconds,
                audio.IsUsingCache,
                request.AudioRequest.SessionId),
            cancellationToken).ConfigureAwait(false);

        return new PlaybackSegmentRunResult(
            audio,
            _localAudioPlaybackCoordinator.CurrentSnapshot);
    }
}
