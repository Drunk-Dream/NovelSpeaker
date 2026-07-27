using NAudio.Wave;

namespace NovelSpeaker.Infrastructure.Playback.Export;

/// <summary>
/// Normalizes decoded source audio and encodes one chapter through Windows Media Foundation.
/// </summary>
internal sealed class MediaFoundationChapterMp3Encoder : IChapterMp3Encoder
{
    private const int Mp3BitRate = 128_000;

    public Task EncodeAsync(
        IReadOnlyList<string> sourceFilePaths,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePaths);
        ArgumentNullException.ThrowIfNull(destination);
        if (sourceFilePaths.Count == 0)
        {
            throw new ArgumentException("At least one source audio file is required.", nameof(sourceFilePaths));
        }

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var provider = new SequentialNormalizedWaveProvider(
                    sourceFilePaths,
                    cancellationToken);
                MediaFoundationEncoder.EncodeToMp3(provider, destination, Mp3BitRate);
                cancellationToken.ThrowIfCancellationRequested();
            },
            cancellationToken);
    }
}
