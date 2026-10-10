using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Navigation;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed partial class PlayerViewTests
{
    [Fact]
    public void Player_provider_popup_opens_with_an_available_provider()
    {
        WpfTestHost.RunInSta(() =>
        {
            var contentService = new FakeBookPlaybackContentService(
                new PlaybackBookContent(
                    "book-1",
                    "示例小说",
                    [PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "脱敏示例段落", "脱敏示例段落")])],
                    "示例作者"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "脱敏示例段落", "脱敏示例段落")]));
            var viewModel = new PlayerViewModel(
                new FakePlaybackCoordinator(new PlaybackSnapshot(
                    PlaybackState.Paused,
                    "book-1",
                    "示例小说",
                    0,
                    "第一章",
                    0,
                    1,
                    TestSpeechProviders.Id(1),
                    "回归测试 Provider",
                    10,
                    0,
                    0,
                    null,
                    false,
                    false,
                    "示例作者")),
                new FakePlaybackStopTimer(),
                new FakeActiveCacheCoordinator(),
                new PlaybackBackedBookDetailsQuery(contentService),
                contentService,
                new FakeProviders([TestSpeechProviders.Item(1, "回归测试 Provider", true)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new FakePlayerAutoScrollCoordinator(),
                new CacheReadModelTestDouble(),
                new FakeMiniPlayerLauncher());
            viewModel.OnPageNavigatedTo(new PageActivationController().Activate());
            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 800, Height = 600, ShowInTaskbar = false };
            using var host = WpfWindowHost.Show(window);
            var popup = Assert.IsType<Popup>(view.FindName("ProviderMenuPopup"));

            popup.IsOpen = true;
            popup.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });

            Assert.Single(viewModel.Providers);
            Assert.True(popup.IsOpen);
            popup.IsOpen = false;
        });
    }
}
