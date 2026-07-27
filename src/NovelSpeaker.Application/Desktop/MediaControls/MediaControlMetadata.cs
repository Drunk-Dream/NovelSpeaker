namespace NovelSpeaker.Application.Desktop.MediaControls;

public sealed record MediaControlMetadata(
    string ChapterTitle,
    string BookTitle,
    MediaControlPlaybackStatus PlaybackStatus);
