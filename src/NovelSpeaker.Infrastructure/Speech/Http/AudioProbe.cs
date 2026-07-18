using NAudio.Wave;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>Uses the production decoder to prove that a downloaded file is playable.</summary>
public sealed class AudioProbe
{
    public bool CanDecode(string filePath)
    {
        try
        {
            using var reader = new AudioFileReader(filePath);
            return reader.TotalTime > TimeSpan.Zero;
        }
        catch
        {
            return false;
        }
    }
}
