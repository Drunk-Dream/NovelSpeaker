using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.Playback.Presentation;

internal sealed class PlayerSnapshotProjection
{
    public PlayerSnapshotViewState Project(
        PlaybackSnapshot snapshot,
        PlaybackBookContent? loadedBook,
        string fallbackChapterTitle,
        int defaultSpeakSpeed)
    {
        var isFaulted = snapshot.State == PlaybackState.Faulted;
        return new PlayerSnapshotViewState(
            snapshot.State,
            string.IsNullOrWhiteSpace(snapshot.BookTitle)
                ? loadedBook?.BookTitle ?? "未打开书籍"
                : snapshot.BookTitle,
            string.IsNullOrWhiteSpace(snapshot.BookAuthor)
                ? string.IsNullOrWhiteSpace(loadedBook?.BookAuthor) ? "未知作者" : loadedBook.BookAuthor!
                : snapshot.BookAuthor,
            string.IsNullOrWhiteSpace(snapshot.ChapterTitle)
                ? fallbackChapterTitle
                : snapshot.ChapterTitle,
            isFaulted,
            snapshot.HasAvailableRule,
            isFaulted ? snapshot.Message ?? "播放失败。" : string.Empty,
            snapshot.State == PlaybackState.Playing ? "暂停" : "播放",
            AppSettings.NormalizeSpeakSpeed(
                string.IsNullOrWhiteSpace(snapshot.BookId) || snapshot.SpeakSpeed <= 0
                    ? defaultSpeakSpeed
                    : snapshot.SpeakSpeed),
            string.IsNullOrWhiteSpace(snapshot.BookId) ? -1 : snapshot.ChapterIndex,
            string.IsNullOrWhiteSpace(snapshot.BookId) ? -1 : snapshot.SegmentIndex);
    }
}

internal sealed record PlayerSnapshotViewState(
    PlaybackState PlaybackState,
    string Title,
    string Author,
    string ChapterTitle,
    bool IsFaulted,
    bool HasAvailableRule,
    string ErrorText,
    string PrimaryActionText,
    int SpeakSpeed,
    int ChapterIndex,
    int SegmentIndex);
