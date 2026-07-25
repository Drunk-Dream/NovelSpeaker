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

namespace NovelSpeaker.UnitTests.Ui;

public sealed partial class PlayerViewTests
{
    [Fact]
    public void PlayerPage_first_navigation_centers_restored_current_segment_after_initial_layout()
    {
        WpfTestHost.RunInSta(() =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Paused,
                "book-1",
                "信息全知者",
                0,
                "第三章 来自星空的压力",
                12,
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
                    .Select(index => new SpeechSegment(
                        index,
                        index * 10,
                        10,
                        $"第 {index + 1} 段",
                        string.Join(' ', Enumerable.Repeat($"这是第 {index + 1} 段长度不同的正文", (index % 7) + 1))))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new FakePlayerAutoScrollCoordinator());

            var page = new PlayerPage(viewModel)
            {
                DataContext = new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            };

            Assert.False(page.IsLoaded);
            page.OnNavigatedToAsync().GetAwaiter().GetResult();
            Assert.False(page.IsLoaded);
            var window = new Window
            {
                Content = page,
                Width = 1280,
                Height = 760,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            try
            {
                window.Show();
                DoEvents();
                page.UpdateLayout();

                Pump(TimeSpan.FromMilliseconds(80));
                var view = Assert.IsType<PlayerView>(VisualTreeTestHelper.FindDescendant<PlayerView>(page));
                WaitUntil(() => !view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(1200));
                view.UpdateLayout();
                DoEvents();

                var segmentsListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));
                var currentContainer = Assert.IsAssignableFrom<FrameworkElement>(
                    segmentsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem));

                var itemTop = currentContainer.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                var itemCenter = itemTop + (currentContainer.ActualHeight / 2d);
                var viewportCenter = scrollViewer.ViewportHeight / 2d;

                Assert.True(scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 1d);
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 1d);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PlayerView_return_to_current_segment_recenters_instead_of_sticking_to_bottom()
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
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证回到当前段不会把列表滚到最底部。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System));

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel,
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
                window.Show();
                DoEvents();
                view.UpdateLayout();

                var segmentsListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));
                scrollViewer.ScrollToBottom();
                DoEvents();
                view.UpdateLayout();

                Assert.True(viewModel.ShowReturnToCurrentSegment);
                Assert.True(scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1d);

                viewModel.ReturnToCurrentSegmentCommand.Execute(null);
                DoEvents();
                view.UpdateLayout();
                DoEvents();

                var currentContainer = Assert.IsAssignableFrom<FrameworkElement>(
                    segmentsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem));
                var itemTop = currentContainer.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                var itemCenter = itemTop + (currentContainer.ActualHeight / 2d);
                var viewportCenter = scrollViewer.ViewportHeight / 2d;

                Assert.False(viewModel.ShowReturnToCurrentSegment);
                Assert.True(scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 1d);
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 1d);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PlayerView_manual_browsing_does_not_recenter_when_playback_auto_advances()
    {
        WpfTestHost.RunInSta(() =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Playing,
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
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证播放自动切段不会抢回用户滚动位置。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System));

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel,
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
                window.Show();
                DoEvents();
                view.UpdateLayout();

                var segmentsListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));
                scrollViewer.ScrollToBottom();
                DoEvents();
                view.UpdateLayout();

                var offsetBeforeAutoAdvance = scrollViewer.VerticalOffset;
                Assert.True(viewModel.ShowReturnToCurrentSegment);

                coordinator.Publish(coordinator.CurrentSnapshot with
                {
                    SegmentIndex = 19,
                    SegmentCount = 90
                });

                DoEvents();
                view.UpdateLayout();
                DoEvents();

                Assert.True(viewModel.ShowReturnToCurrentSegment);
                Assert.InRange(Math.Abs(scrollViewer.VerticalOffset - offsetBeforeAutoAdvance), 0d, 1d);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PlayerView_user_scroll_during_animation_cancels_centering_and_shows_return_button()
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
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证滚动输入会中断动画并显示恢复居中按钮。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System));

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel,
                SegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(500),
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
                window.Show();
                DoEvents();
                view.UpdateLayout();

                coordinator.Publish(coordinator.CurrentSnapshot with
                {
                    SegmentIndex = 70,
                    SegmentCount = 90
                });

                WaitUntil(() => view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(500));

                var segmentsListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
                segmentsListBox.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                });
                DoEvents();

                Assert.False(view.HasActiveSegmentScrollAnimation);
                Assert.True(viewModel.ShowReturnToCurrentSegment);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PlayerView_next_segment_recenters_after_active_navigation()
    {
        WpfTestHost.RunInSta(() =>
        {
            var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Paused,
                "book-1",
                "信息全知者",
                0,
                "第三章 来自星空的压力",
                70,
                138,
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
                Enumerable.Range(0, 138)
                    .Select(index => new SpeechSegment(
                        index,
                        index * 10,
                        10,
                        $"第 {index + 1} 段",
                        string.Join(' ', Enumerable.Repeat($"这是第 {index + 1} 段长度不同的正文，用来模拟实际章节中长短差异很大的段落。", (index % 17) + 1))))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [PlaybackChapterContent.FromLoaded(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new PlayerAutoScrollCoordinator(TimeProvider.System));

            viewModel.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
                CancellationToken.None).GetAwaiter().GetResult();

            var view = new PlayerView
            {
                DataContext = viewModel,
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
                window.Show();
                DoEvents();
                view.UpdateLayout();

                var segmentsListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));
                var observedOffsets = new List<double>();
                ScrollChangedEventHandler captureOffset = (_, eventArgs) =>
                {
                    if (eventArgs.VerticalChange != 0)
                    {
                        observedOffsets.Add(eventArgs.VerticalOffset);
                    }
                };
                scrollViewer.ScrollChanged += captureOffset;

                Pump(TimeSpan.FromMilliseconds(300));
                observedOffsets.Clear();
                var centerRequestCount = 0;
                PropertyChangedEventHandler captureCenterRequest = (_, eventArgs) =>
                {
                    if (eventArgs.PropertyName == nameof(PlayerViewModel.SegmentCenterRequestVersion))
                    {
                        centerRequestCount++;
                    }
                };
                viewModel.PropertyChanged += captureCenterRequest;

                viewModel.NextSegmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Pump(TimeSpan.FromMilliseconds(80));
                WaitUntil(() => !view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(1200));
                Pump(TimeSpan.FromMilliseconds(100));
                view.UpdateLayout();
                DoEvents();

                var currentContainer = Assert.IsAssignableFrom<FrameworkElement>(
                    segmentsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem));
                var itemTop = currentContainer.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                var itemCenter = itemTop + (currentContainer.ActualHeight / 2d);
                var viewportCenter = scrollViewer.ViewportHeight / 2d;

                Assert.False(viewModel.ShowReturnToCurrentSegment);
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 1d);
                Assert.Equal(1, centerRequestCount);
                Assert.All(observedOffsets.Zip(observedOffsets.Skip(1)), pair => Assert.True(pair.Second >= pair.First));
                viewModel.PropertyChanged -= captureCenterRequest;
                scrollViewer.ScrollChanged -= captureOffset;
            }
            finally
            {
                window.Close();
            }
        });
    }

}
