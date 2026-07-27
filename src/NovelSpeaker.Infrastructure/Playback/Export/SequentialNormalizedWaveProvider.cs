using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NovelSpeaker.Infrastructure.Playback.Export;

/// <summary>
/// Opens one source at a time and projects every segment to the same float PCM format.
/// </summary>
internal sealed class SequentialNormalizedWaveProvider : IWaveProvider, IDisposable
{
    private const int TargetSampleRate = 44_100;
    private const int TargetChannelCount = 2;
    private readonly IReadOnlyList<string> _sourceFilePaths;
    private readonly CancellationToken _cancellationToken;
    private int _nextSourceIndex;
    private AudioFileReader? _currentReader;
    private IWaveProvider? _currentProvider;

    public SequentialNormalizedWaveProvider(
        IReadOnlyList<string> sourceFilePaths,
        CancellationToken cancellationToken)
    {
        _sourceFilePaths = sourceFilePaths;
        _cancellationToken = cancellationToken;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, TargetChannelCount);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            if (_currentProvider is null && !OpenNextSource())
            {
                return 0;
            }

            var read = _currentProvider!.Read(buffer, offset, count);
            _cancellationToken.ThrowIfCancellationRequested();
            if (read > 0)
            {
                return read;
            }

            CloseCurrentSource();
        }
    }

    public void Dispose()
    {
        CloseCurrentSource();
    }

    private bool OpenNextSource()
    {
        if (_nextSourceIndex >= _sourceFilePaths.Count)
        {
            return false;
        }

        _cancellationToken.ThrowIfCancellationRequested();
        var reader = new AudioFileReader(_sourceFilePaths[_nextSourceIndex++]);
        try
        {
            ISampleProvider samples = reader;
            samples = samples.WaveFormat.Channels switch
            {
                1 => new MonoToStereoSampleProvider(samples),
                2 => samples,
                _ => throw new InvalidDataException(
                    $"Unsupported source channel count: {samples.WaveFormat.Channels}.")
            };
            if (samples.WaveFormat.SampleRate != TargetSampleRate)
            {
                samples = new WdlResamplingSampleProvider(samples, TargetSampleRate);
            }

            _currentReader = reader;
            _currentProvider = samples.ToWaveProvider();
            return true;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private void CloseCurrentSource()
    {
        _currentProvider = null;
        _currentReader?.Dispose();
        _currentReader = null;
    }
}
