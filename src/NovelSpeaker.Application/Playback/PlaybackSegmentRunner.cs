using NovelSpeaker.Application.Cache.Audio;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Describes the inputs needed to prepare and play one current segment.
/// </summary>
internal sealed record PlaybackSegmentRunRequest(
    AudioGenerationRequest AudioRequest,
    string DisplayTitle,
    long ResumePositionMilliseconds,
    bool ForceInvalidate,
    Func<CancellationToken, Task>? ValidateContextAsync = null);

/// <summary>
/// Captures the result of one segment execution without owning playback state or publishing events.
/// </summary>
internal sealed record PlaybackSegmentRunResult(
    AudioGenerationResult Audio,
    LocalAudioPlaybackSnapshot LocalSnapshot);

/// <summary>
/// Obtains one segment's audio and hands it to the local audio coordinator.
/// Long-lived session state and UI projection remain owned by <see cref="PlaybackCoordinator"/>.
/// </summary>
internal sealed class PlaybackSegmentRunner
{
    private readonly IAudioGenerationProvider _audioProvider;
    private readonly ILocalAudioPlaybackCoordinator _localAudio;

    public PlaybackSegmentRunner(
        IAudioGenerationProvider audioProvider,
        ILocalAudioPlaybackCoordinator localAudio)
    {
        _audioProvider = audioProvider;
        _localAudio = localAudio;
    }

    public async Task<AudioGenerationResult> PrepareAsync(
        PlaybackSegmentRunRequest request,
        Action<AudioGenerationProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ForceInvalidate)
        {
            await _audioProvider.InvalidateAsync(
                request.AudioRequest,
                cancellationToken).ConfigureAwait(false);
        }

        return await _audioProvider.GetAudioAsync(
            request.AudioRequest,
            AudioGenerationPriority.Current,
            progressCallback,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlaybackSegmentRunResult> PlayPreparedAsync(
        PlaybackSegmentRunRequest request,
        AudioGenerationResult audio,
        CancellationToken cancellationToken)
    {
        if (!audio.IsSuccess)
        {
            return new PlaybackSegmentRunResult(audio, _localAudio.CurrentSnapshot);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (request.ValidateContextAsync is not null)
            await request.ValidateContextAsync(cancellationToken).ConfigureAwait(false);

        await _localAudio.StartAsync(
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

        return new PlaybackSegmentRunResult(audio, _localAudio.CurrentSnapshot);
    }
}
