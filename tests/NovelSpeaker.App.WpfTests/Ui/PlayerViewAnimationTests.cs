using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using Wpf.Ui;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed partial class PlayerViewTests
{
    [Fact]
    public void PlayerView_consecutive_navigation_requests_cancel_previous_segment_animation()
    {
        WpfTestHost.RunInSta(() =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Paused,
                "book-1",
                "信息全知者",
                0,
                "第三章 来自星空的压力",
                18,
                90,
                1,
                "默认规则",
                10,
                0,
                0,
                null,
                false,
                false,
                "魔性沧月",
                true));
            var chapter = PlaybackChapterContent.FromLoaded(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证连续跳转会取消旧动画。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new WpfFakePlaybackStopTimer(),
                new WpfFakeActiveCacheCoordinator(),
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System),
                new FakeCacheWorkspaceService(),
                new WpfFakeMiniPlayerLauncher());

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel,
                SegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(260),
                ReduceMotionOverride = false
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
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));

                scrollViewer.ScrollToBottom();
                DoEvents();
                view.UpdateLayout();

                viewModel.NextSegmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                WaitUntil(() => view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(400));

                viewModel.NextSegmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Pump(TimeSpan.FromMilliseconds(80));
                WaitUntil(() => !view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(1200));
                DoEvents();
                view.UpdateLayout();

                var currentContainer = Assert.IsAssignableFrom<FrameworkElement>(
                    segmentsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem));
                var itemTop = currentContainer.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                var itemCenter = itemTop + (currentContainer.ActualHeight / 2d);
                var viewportCenter = scrollViewer.ViewportHeight / 2d;

                Assert.Equal(20, viewModel.CurrentSegmentIndex);
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 1d);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PlayerView_reduce_motion_prefers_direct_positioning_over_animation()
    {
        WpfTestHost.RunInSta(() =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Paused,
                "book-1",
                "信息全知者",
                0,
                "第三章 来自星空的压力",
                18,
                90,
                1,
                "默认规则",
                10,
                0,
                0,
                null,
                false,
                false,
                "魔性沧月",
                true));
            var chapter = PlaybackChapterContent.FromLoaded(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证减少动画时直接定位。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new WpfFakePlaybackStopTimer(),
                new WpfFakeActiveCacheCoordinator(),
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System),
                new FakeCacheWorkspaceService(),
                new WpfFakeMiniPlayerLauncher());

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel,
                SegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(260),
                ReduceMotionOverride = true
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
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));

                scrollViewer.ScrollToBottom();
                DoEvents();
                view.UpdateLayout();

                viewModel.NextSegmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                DoEvents();
                view.UpdateLayout();

                var currentContainer = Assert.IsAssignableFrom<FrameworkElement>(
                    segmentsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem));
                var itemTop = currentContainer.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                var itemCenter = itemTop + (currentContainer.ActualHeight / 2d);
                var viewportCenter = scrollViewer.ViewportHeight / 2d;

                Assert.False(view.HasActiveSegmentScrollAnimation);
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 1d);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PlayerView_virtualized_target_moves_toward_center_without_direction_reversal()
    {
        WpfTestHost.RunInSta(() =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Playing,
                "book-1",
                "信息全知者",
                0,
                "第三章 来自星空的压力",
                2,
                120,
                1,
                "默认规则",
                10,
                0,
                0,
                null,
                false,
                false,
                "魔性沧月",
                true));
            var chapter = PlaybackChapterContent.FromLoaded(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 120)
                    .Select(index => new SpeechSegment(
                        index,
                        index * 10,
                        10,
                        $"第 {index + 1} 段",
                        string.Join(' ', Enumerable.Repeat(
                            $"这是第 {index + 1} 段长度差异很大的正文，用来验证虚拟化目标延迟生成时不会越过目标再反向回滚。",
                            ((index * 7) % 19) + 1))))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new WpfFakePlaybackStopTimer(),
                new WpfFakeActiveCacheCoordinator(),
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System),
                new FakeCacheWorkspaceService(),
                new WpfFakeMiniPlayerLauncher());

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel,
                SegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(180),
                ReduceMotionOverride = false
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
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));
                var observedOffsets = new List<double>();
                var originalSegmentItems = viewModel.Segments.ToArray();
                var collectionChanges = new List<NotifyCollectionChangedAction>();
                ScrollChangedEventHandler captureOffset = (_, eventArgs) =>
                {
                    if (eventArgs.VerticalChange != 0)
                    {
                        observedOffsets.Add(eventArgs.VerticalOffset);
                    }
                };
                scrollViewer.ScrollChanged += captureOffset;
                viewModel.Segments.CollectionChanged += (_, eventArgs) => collectionChanges.Add(eventArgs.Action);

                coordinator.Publish(coordinator.CurrentSnapshot with
                {
                    State = PlaybackState.Buffering,
                    SegmentIndex = 88,
                    SegmentCount = 120
                });

                WaitUntil(() => view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(500));
                coordinator.Publish(coordinator.CurrentSnapshot with { State = PlaybackState.Preparing });
                coordinator.Publish(coordinator.CurrentSnapshot with { State = PlaybackState.Playing });
                WaitUntil(() => !view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(1200));
                DoEvents();
                view.UpdateLayout();

                var currentContainer = Assert.IsAssignableFrom<FrameworkElement>(
                    segmentsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem));
                var itemTop = currentContainer.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                var itemCenter = itemTop + (currentContainer.ActualHeight / 2d);
                var viewportCenter = scrollViewer.ViewportHeight / 2d;

                Assert.Equal(88, viewModel.CurrentSegmentIndex);
                Assert.Empty(collectionChanges);
                Assert.All(originalSegmentItems.Select((item, index) => (item, index)), pair =>
                    Assert.Same(pair.item, viewModel.Segments[pair.index]));
                Assert.Same(originalSegmentItems[88], viewModel.CurrentSegmentItem);
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 1d);
                Assert.All(observedOffsets.Zip(observedOffsets.Skip(1)), pair => Assert.True(pair.Second >= pair.First));
                scrollViewer.ScrollChanged -= captureOffset;
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PlayerView_page_unload_cancels_active_segment_animation()
    {
        WpfTestHost.RunInSta(() =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Paused,
                "book-1",
                "信息全知者",
                0,
                "第三章 来自星空的压力",
                18,
                90,
                1,
                "默认规则",
                10,
                0,
                0,
                null,
                false,
                false,
                "魔性沧月",
                true));
            var chapter = PlaybackChapterContent.FromLoaded(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证页面离开会取消滚动动画。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new WpfFakePlaybackStopTimer(),
                new WpfFakeActiveCacheCoordinator(),
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System),
                new FakeCacheWorkspaceService(),
                new WpfFakeMiniPlayerLauncher());

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel,
                SegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(260),
                ReduceMotionOverride = false
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

            WpfWindowHost.Show(window);
            DoEvents();
            view.UpdateLayout();

            var segmentsListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
            var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));

            scrollViewer.ScrollToBottom();
            DoEvents();
            view.UpdateLayout();

            viewModel.NextSegmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            WaitUntil(() => view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(400));

            window.Close();
            DoEvents();

            Assert.False(view.HasActiveSegmentScrollAnimation);
        });
    }
}
