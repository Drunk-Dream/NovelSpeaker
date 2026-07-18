namespace NovelSpeaker.Application.Playback.Audio;

/// <summary>Maps local playback exceptions to stable, user-safe classifications and messages.</summary>
public static class PlaybackErrorMapper
{
    public static PlaybackErrorEventArgs Map(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return new PlaybackErrorEventArgs(PlaybackErrorKind.Cancelled, "音频加载已取消。");
        }

        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new PlaybackErrorEventArgs(PlaybackErrorKind.FileNotFound, "找不到要播放的音频文件。");
        }

        if (exception.GetType().Name.Contains("MmException", StringComparison.OrdinalIgnoreCase))
        {
            return new PlaybackErrorEventArgs(PlaybackErrorKind.OutputDevice, "当前设备无法输出音频。");
        }

        if (exception is InvalidDataException or FormatException)
        {
            return new PlaybackErrorEventArgs(PlaybackErrorKind.UnsupportedFormat, "当前音频格式不受支持。");
        }

        if (exception.GetType().Name.Contains("Mp3", StringComparison.OrdinalIgnoreCase) ||
            exception.GetType().Name.Contains("Wave", StringComparison.OrdinalIgnoreCase))
        {
            return new PlaybackErrorEventArgs(PlaybackErrorKind.AudioDecode, "音频解码失败，请更换音频文件后重试。");
        }

        return new PlaybackErrorEventArgs(PlaybackErrorKind.Unknown, "本地音频播放失败，请重试。");
    }
}
