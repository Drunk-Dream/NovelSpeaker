using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Navigation;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed partial class PlayerViewTests
{
    [Fact]
    public async Task Player_follows_wrapped_paragraphs_after_resize_and_respects_manual_browsing()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var text = string.Concat(Enumerable.Repeat("这是用于阅读定位回归的虚构正文。", 12));
            var segments = Enumerable.Range(0, 200)
                .Select(index => new SpeechSegment(index, index * text.Length, text.Length, text, text)).ToArray();
            var chapter = PlaybackChapterContent.FromLoaded(0, "虚构章节", segments);
            var content = new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "虚构书名", [chapter]), chapter);
            var playback = new FakePlaybackCoordinator(PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Playing,
                BookId = "book-1",
                ChapterIndex = 0,
                SegmentIndex = 80,
                SegmentCount = segments.Length,
                ProviderId = TestSpeechProviders.Id(1)
            });
            using var scrolling = new PlayerAutoScrollCoordinator(new ManualTimeProvider());
            var viewModel = new PlayerViewModel(
                playback, new FakePlaybackStopTimer(), new FakeActiveCacheCoordinator(),
                new PlaybackBackedBookDetailsQuery(content), content,
                new FakeProviders([TestSpeechProviders.Item(1, "虚构服务", true)]),
                new FakeAppSettingsStore(AppSettings.Default), new FakeAppFeedbackService(),
                new FakeNavigationService(), scrolling, new CacheReadModelTestDouble(), new FakeMiniPlayerLauncher());
            using var activation = new PageActivationController();
            viewModel.OnPageNavigatedTo(activation.Activate());
            try
            {
                await viewModel.LoadAsync(CancellationToken.None);
                await viewModel.HandleNavigationAsync(
                    new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
                    CancellationToken.None);
                var view = new PlayerView { DataContext = viewModel, ReduceMotionOverride = true };
                var window = new Window { Content = view, Width = 1280, Height = 820, ShowInTaskbar = false };
                using var host = WpfWindowHost.Show(window);
                var list = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
                var scroll = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(list));

                await WaitUntilAsync(CurrentParagraphIsVisible);
                foreach (var width in new[] { 900d, 1450d, 900d })
                {
                    window.Width = width;
                    await DrainAsync();
                    await WaitUntilAsync(CurrentParagraphIsVisible);
                    Assert.True(viewModel.ShouldAutoCenterCurrentSegment);
                }

                list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                {
                    RoutedEvent = Mouse.PreviewMouseWheelEvent
                });
                scroll.ScrollToBottom();
                await DrainAsync();
                Assert.True(viewModel.ShowReturnToCurrentSegment);
                playback.Publish(playback.CurrentSnapshot with { SegmentIndex = 81, TargetRevision = 1 });
                window.Width = 1280;
                await DrainAsync();
                Assert.False(viewModel.ShouldAutoCenterCurrentSegment);
                Assert.False(CurrentParagraphIsVisible());

                viewModel.ReturnToCurrentSegmentCommand.Execute(null);
                await WaitUntilAsync(CurrentParagraphIsVisible);
                Assert.True(viewModel.ShouldAutoCenterCurrentSegment);
                view.ReduceMotionOverride = false;
                playback.Publish(playback.CurrentSnapshot with { SegmentIndex = 82, TargetRevision = 2 });
                await WaitUntilAsync(() => view.HasActiveSegmentScrollAnimation);
                await WaitUntilAsync(() => !view.HasActiveSegmentScrollAnimation && CurrentParagraphIsVisible());
                Assert.True(viewModel.ShouldAutoCenterCurrentSegment);

                bool CurrentParagraphIsVisible()
                {
                    if (list.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem) is not FrameworkElement item)
                        return false;
                    var top = item.TranslatePoint(new Point(), scroll).Y;
                    return top >= 0 && top < scroll.ViewportHeight &&
                           top + item.ActualHeight <= Math.Max(scroll.ViewportHeight, item.ActualHeight) + 1;
                }

                async Task DrainAsync()
                {
                    window.UpdateLayout();
                    await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                }

                async Task WaitUntilAsync(Func<bool> predicate)
                {
                    var deadline = Stopwatch.StartNew();
                    do
                    {
                        await DrainAsync();
                        if (predicate()) return;
                    } while (deadline.Elapsed < TimeSpan.FromSeconds(5));
                    Assert.True(predicate(), "The current paragraph did not return to the reading viewport.");
                }
            }
            finally
            {
                viewModel.OnPageNavigatedFrom();
            }
        });
    }
}
