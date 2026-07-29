namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Normalizes the application's playback volume without affecting the operating system volume.
/// </summary>
public static class PlaybackVolume
{
    public const double Default = 1d;

    public static double Normalize(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, 0d, 1d)
            : Default;
    }
}
