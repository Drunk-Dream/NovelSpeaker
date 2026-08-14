using System.Windows;
using System.Windows.Controls;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Features.Playback.Components;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed partial class PlayerViewTests
{
    [Fact]
    public void PlayerView_switching_chapters_scrolls_preview_to_top_when_title_is_read()
    {
        WpfTestHost.RunInSta(() =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Paused,
                "book-1",
                "信息全知者",
                0,
                "第一章",
                20,
                81,
                1,
                "默认规则",
                10,
                0,
                0,
                null,
                false,
                false,
                "魔性沧月"));
            var chapters = new[]
            {
                CreateChapter(0, "第一章"),
                CreateChapter(1, "第二章")
            };
            var viewModel = new PlayerViewModel(
                coordinator,
                new WpfFakePlaybackStopTimer(),
                new WpfFakeActiveCacheCoordinator(),
                new ChapterMapPlaybackContentService(
                    new PlaybackBookContent(
                        "book-1",
                        "信息全知者",
                        chapters.Select(chapter => PlaybackChapterContent.Unloaded(chapter.ChapterIndex, chapter.Title)).ToArray(),
                        "魔性沧月"),
                    chapters),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default with { ReadChapterTitle = true }),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System),
                new FakeCacheWorkspaceService(),
                new WpfFakeMiniPlayerLauncher());

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel
            };
            var window = new Window
            {
                Content = view,
                Width = 1280,
                Height = 760,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            try
            {
                WpfWindowHost.Show(window);
                DoEvents();
                view.UpdateLayout();

                var segmentsListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                    VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));
                scrollViewer.ScrollToBottom();
                DoEvents();
                view.UpdateLayout();
                Assert.True(scrollViewer.VerticalOffset > 0);

                viewModel.HandleChapterClickAsync(
                    viewModel.Chapters[1],
                    DesktopSelectionModifiers.None,
                    CancellationToken.None).GetAwaiter().GetResult();

                WaitUntil(
                    () => viewModel.CurrentChapterIndex == 1 &&
                          viewModel.Segments.Count == 80 &&
                          viewModel.CurrentSegmentItem is null,
                    TimeSpan.FromSeconds(2));
                WaitUntil(() => !view.HasActiveSegmentScrollAnimation, TimeSpan.FromSeconds(2));
                view.UpdateLayout();
                DoEvents();

                Assert.Equal(0, scrollViewer.VerticalOffset);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static PlaybackChapterContent CreateChapter(int chapterIndex, string title)
    {
        return PlaybackChapterContent.FromLoaded(
            chapterIndex,
            title,
            [
                new SpeechSegment(0, 0, 0, title, title, IsChapterTitle: true),
                .. Enumerable.Range(1, 80).Select(index => new SpeechSegment(
                    index,
                    index * 10,
                    10,
                    $"第 {index} 段",
                    $"这是第 {index} 段正文。"))
            ]);
    }

    private sealed class ChapterMapPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent _book;
        private readonly IReadOnlyDictionary<int, PlaybackChapterContent> _chapters;

        public ChapterMapPlaybackContentService(
            PlaybackBookContent book,
            IReadOnlyList<PlaybackChapterContent> chapters)
        {
            _book = book;
            _chapters = chapters.ToDictionary(chapter => chapter.ChapterIndex);
        }

        public Task<PlaybackBookContent?> GetBookAsync(
            string bookId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackBookContent?>(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_chapters.GetValueOrDefault(chapterIndex));
        }
    }
}
