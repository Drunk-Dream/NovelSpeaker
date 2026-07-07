using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Player;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Views;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using Wpf.Ui;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed class PlayerViewTests
{
    [Fact]
    public void PlayerView_keeps_catalog_and_segments_scrollable_inside_their_cards()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>();
            for (var chapterIndex = 0; chapterIndex < 80; chapterIndex++)
            {
                var chapter = new PlayerChapterItemViewModel(chapterIndex, $"第{chapterIndex + 1}章 标题较长用于验证目录内部滚动");
                chapter.IsCurrent = chapterIndex == 10;
                chapters.Add(chapter);
            }

            var segments = new ObservableCollection<PlayerSegmentItemViewModel>();
            for (var segmentIndex = 0; segmentIndex < 140; segmentIndex++)
            {
                var segment = new PlayerSegmentItemViewModel(
                    10,
                    segmentIndex,
                    $"这是第 {segmentIndex + 1} 段，用来验证正文预览在固定高度下保持内部滚动，而不是继续把整个页面撑高。");
                segment.IsCurrent = segmentIndex == 32;
                segment.VisualOpacity = segmentIndex == 32 ? 1d : 0.52d;
                segments.Add(segment);
            }

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var chaptersListBox = Assert.IsType<ListBox>(view.FindName("WideChaptersListBox"));
            var segmentListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));

            Assert.True(chaptersListBox.ActualHeight > 0);
            Assert.True(segmentListBox.ActualHeight > 0);

            var chaptersScrollViewer = VisualTreeTestHelper.FindDescendant<ScrollViewer>(chaptersListBox);
            var segmentsScrollViewer = VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentListBox);

            Assert.NotNull(chaptersScrollViewer);
            Assert.NotNull(segmentsScrollViewer);
            Assert.True(chaptersScrollViewer!.ScrollableHeight > 0);
            Assert.True(segmentsScrollViewer!.ScrollableHeight > 0);
        });
    }

    [Fact]
    public void PlayerView_keeps_playback_footer_visible_with_long_content()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>();
            for (var chapterIndex = 0; chapterIndex < 120; chapterIndex++)
            {
                var chapter = new PlayerChapterItemViewModel(chapterIndex, $"第{chapterIndex + 1}章");
                chapter.IsCurrent = chapterIndex == 10;
                chapters.Add(chapter);
            }

            var segments = new ObservableCollection<PlayerSegmentItemViewModel>();
            for (var segmentIndex = 0; segmentIndex < 180; segmentIndex++)
            {
                var segment = new PlayerSegmentItemViewModel(10, segmentIndex, $"第 {segmentIndex + 1} 段");
                segment.IsCurrent = segmentIndex == 32;
                segment.VisualOpacity = segmentIndex == 32 ? 1d : 0.52d;
                segments.Add(segment);
            }

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var footer = Assert.IsType<Border>(view.FindName("PlaybackFooterBorder"));
            var playButton = Assert.IsType<Button>(view.FindName("PrimaryPlaybackButton"));

            Assert.Equal(Visibility.Visible, footer.Visibility);
            Assert.True(GetBoundsRelativeToRoot(footer, view).Bottom <= view.ActualHeight);
            Assert.True(GetBoundsRelativeToRoot(playButton, view).Bottom <= view.ActualHeight);
        });
    }

    [Fact]
    public void PlayerView_places_book_title_in_toolbar_and_moves_chapter_title_to_preview_header()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "第一段")
                {
                    IsCurrent = true,
                    VisualOpacity = 1d
                }
            };

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var backButton = Assert.IsType<Button>(view.FindName("BackButton"));
            var titleText = Assert.IsType<TextBlock>(FindVisibleDescendantByText(view, "信息全知者"));
            var chapterTitleText = Assert.IsType<TextBlock>(FindVisibleDescendantByText(view, "第二章 头铁的落款"));
            var footer = Assert.IsType<Border>(view.FindName("PlaybackFooterBorder"));

            Assert.Null(FindVisibleDescendantByText(view, "魔性沧月"));
            Assert.Null(FindVisibleDescendantByText(footer, "33 / 140"));

            var backBounds = GetBoundsRelativeToRoot(backButton, view);
            var titleBounds = GetBoundsRelativeToRoot(titleText, view);
            var chapterTitleBounds = GetBoundsRelativeToRoot(chapterTitleText, view);

            Assert.InRange(Math.Abs(titleBounds.Top - backBounds.Top), 0d, 10d);
            Assert.True(titleBounds.Left > backBounds.Right);
            Assert.True(chapterTitleBounds.Top > titleBounds.Bottom);
        });
    }

    [Fact]
    public void PlayerView_shows_return_to_current_segment_button_when_manual_browsing()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "第一段")
                {
                    IsCurrent = true,
                    VisualOpacity = 1d
                }
            };

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments, showReturnToCurrentSegment: true),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var returnButton = Assert.IsType<Button>(view.FindName("ReturnToCurrentSegmentButton"));
            Assert.Equal(Visibility.Visible, returnButton.Visibility);
            Assert.Equal("回到当前段", returnButton.ToolTip);
            Assert.Equal("回到当前段", AutomationProperties.GetName(returnButton));
        });
    }

    [Fact]
    public void PlayerView_replaces_control_area_with_no_rule_state()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "第一段")
            };

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(
                    chapters,
                    segments,
                    showPlaybackControls: false,
                    showNoRuleState: true),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var emptyStateButton = Assert.IsType<Button>(FindVisibleDescendantByContent(view, "前往 TTS 规则"));
            var noRuleFooter = Assert.IsType<Border>(view.FindName("NoRuleFooterBorder"));
            var backButton = Assert.IsType<Button>(view.FindName("BackButton"));

            Assert.Equal(Visibility.Visible, emptyStateButton.Visibility);
            Assert.Equal(Visibility.Visible, noRuleFooter.Visibility);
            Assert.True(GetBoundsRelativeToRoot(noRuleFooter, view).Bottom <= view.ActualHeight);
            Assert.True(GetBoundsRelativeToRoot(backButton, view).Top >= 0);
        });
    }

    [Fact]
    public void PlayerView_shows_error_bar_only_when_faulted()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "第一段")
            };

            var faultedView = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(
                    chapters,
                    segments,
                    showPlaybackErrorBar: true,
                    errorText: "网络失败，请稍后重试。"),
            };

            faultedView.Measure(new Size(1280, 760));
            faultedView.Arrange(new Rect(0, 0, 1280, 760));
            faultedView.UpdateLayout();

            Assert.NotNull(FindVisibleDescendantByContent(faultedView, "再次尝试"));
            Assert.NotNull(FindVisibleDescendantByContent(faultedView, "切换规则"));

            var normalView = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            normalView.Measure(new Size(1280, 760));
            normalView.Arrange(new Rect(0, 0, 1280, 760));
            normalView.UpdateLayout();

            Assert.Null(FindVisibleDescendantByContent(normalView, "再次尝试"));
            Assert.Null(FindVisibleDescendantByContent(normalView, "切换规则"));
        });
    }

    [Fact]
    public void PlayerView_keeps_catalog_visible_at_minimum_supported_width()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>();
            for (var chapterIndex = 0; chapterIndex < 40; chapterIndex++)
            {
                var chapter = new PlayerChapterItemViewModel(chapterIndex, $"第{chapterIndex + 1}章");
                chapter.IsCurrent = chapterIndex == 10;
                chapters.Add(chapter);
            }

            var segments = new ObservableCollection<PlayerSegmentItemViewModel>();
            for (var segmentIndex = 0; segmentIndex < 80; segmentIndex++)
            {
                var segment = new PlayerSegmentItemViewModel(10, segmentIndex, $"第 {segmentIndex + 1} 段");
                segment.IsCurrent = segmentIndex == 12;
                segment.VisualOpacity = segmentIndex == 12 ? 1d : 0.52d;
                segments.Add(segment);
            }

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(900, 640));
            view.Arrange(new Rect(0, 0, 900, 640));
            view.UpdateLayout();

            var catalog = Assert.IsType<ListBox>(view.FindName("WideChaptersListBox"));
            var footer = Assert.IsType<Border>(view.FindName("PlaybackFooterBorder"));

            Assert.True(catalog.ActualWidth > 0);
            Assert.Equal(Visibility.Visible, footer.Visibility);
            Assert.True(GetBoundsRelativeToRoot(footer, view).Bottom <= view.ActualHeight);
            Assert.Null(view.FindName("DrawerChaptersListBox"));
        });
    }

    [Fact]
    public void PlayerView_keeps_catalog_and_preview_cards_at_the_same_height_without_error_bar()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>();
            for (var chapterIndex = 0; chapterIndex < 30; chapterIndex++)
            {
                var chapter = new PlayerChapterItemViewModel(chapterIndex, $"第{chapterIndex + 1}章");
                chapter.IsCurrent = chapterIndex == 4;
                chapters.Add(chapter);
            }

            var segments = new ObservableCollection<PlayerSegmentItemViewModel>();
            for (var segmentIndex = 0; segmentIndex < 60; segmentIndex++)
            {
                var segment = new PlayerSegmentItemViewModel(4, segmentIndex, $"第 {segmentIndex + 1} 段");
                segment.IsCurrent = segmentIndex == 8;
                segment.VisualOpacity = segmentIndex == 8 ? 1d : 0.52d;
                segments.Add(segment);
            }

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var catalogBorder = Assert.IsType<Border>(view.FindName("CatalogPanelBorder"));
            var previewBorder = Assert.IsType<Border>(view.FindName("PreviewPanelBorder"));

            Assert.InRange(Math.Abs(catalogBorder.ActualHeight - previewBorder.ActualHeight), 0d, 1d);
        });
    }

    [Fact]
    public void PlayerView_uses_single_line_truncated_chapter_titles_without_horizontal_scroll()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章 这是一个特别长特别长特别长的章节标题用于验证单行截断效果")
                {
                    IsCurrent = true
                }
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "第一段")
                {
                    IsCurrent = true,
                    VisualOpacity = 1d
                }
            };

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(900, 640));
            view.Arrange(new Rect(0, 0, 900, 640));
            view.UpdateLayout();

            var chaptersListBox = Assert.IsType<ListBox>(view.FindName("WideChaptersListBox"));
            chaptersListBox.UpdateLayout();

            var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(chaptersListBox));
            var itemContainer = Assert.IsType<ListBoxItem>(chaptersListBox.ItemContainerGenerator.ContainerFromIndex(0));
            var titleText = FindDescendant<TextBlock>(
                itemContainer,
                static textBlock => textBlock.Text.StartsWith("第一章", StringComparison.Ordinal));

            Assert.NotNull(titleText);
            Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(chaptersListBox));
            Assert.Equal(TextWrapping.NoWrap, titleText!.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, titleText.TextTrimming);
            Assert.NotEqual(Visibility.Visible, scrollViewer.ComputedHorizontalScrollBarVisibility);
        });
    }

    [Fact]
    public void PlayerView_uses_full_width_segment_buttons_for_short_paragraphs()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
                {
                    IsCurrent = true
                }
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "短句")
                {
                    IsCurrent = true,
                    VisualOpacity = 1d
                }
            };

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var segmentListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
            var itemContainer = Assert.IsType<ListBoxItem>(segmentListBox.ItemContainerGenerator.ContainerFromIndex(0));
            var segmentButton = Assert.IsType<Button>(FindDescendant<Button>(itemContainer, static _ => true));

            Assert.InRange(Math.Abs(segmentButton.ActualWidth - itemContainer.ActualWidth), 0d, 1d);
        });
    }

    [Fact]
    public void PlayerView_keeps_rule_and_speed_toolbar_buttons_at_the_same_height()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
                {
                    IsCurrent = true
                }
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "第一段")
                {
                    IsCurrent = true,
                    VisualOpacity = 1d
                }
            };

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var ruleButton = Assert.IsType<Button>(view.FindName("RuleMenuButton"));
            var speedButton = Assert.IsType<Button>(view.FindName("SpeedMenuButton"));

            Assert.InRange(Math.Abs(ruleButton.ActualHeight - speedButton.ActualHeight), 0d, 1d);
        });
    }

    [Fact]
    public void PlayerView_uses_accent_filled_segment_progress_slider()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
                {
                    IsCurrent = true
                }
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "第一段")
                {
                    IsCurrent = true,
                    VisualOpacity = 1d
                }
            };

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var fillBar = Assert.IsType<ProgressBar>(view.FindName("SegmentProgressFillBar"));
            var slider = Assert.IsType<Slider>(view.FindName("SegmentProgressSlider"));
            var accentBrush = Assert.IsType<SolidColorBrush>(System.Windows.Application.Current.TryFindResource("AccentFillColorDefaultBrush"));
            var trackBrush = Assert.IsType<SolidColorBrush>(System.Windows.Application.Current.TryFindResource("LayerFillColorAltBrush"));
            var fillBarForeground = Assert.IsType<SolidColorBrush>(fillBar.Foreground);
            var fillBarBackground = Assert.IsType<SolidColorBrush>(fillBar.Background);

            Assert.Equal(accentBrush.Color, fillBarForeground.Color);
            Assert.Equal(trackBrush.Color, fillBarBackground.Color);
            Assert.Equal(slider.Maximum, fillBar.Maximum);
            Assert.Equal(slider.Value, fillBar.Value);
            Assert.True(fillBar.Value > 0);
            Assert.True(fillBar.Maximum > fillBar.Value);
        });
    }

    [Fact]
    public void PlayerView_uses_icon_buttons_with_accessible_metadata_for_playback_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
                {
                    IsCurrent = true
                }
            };
            var segments = new ObservableCollection<PlayerSegmentItemViewModel>
            {
                new(0, 0, "第一段")
                {
                    IsCurrent = true,
                    VisualOpacity = 1d
                }
            };

            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments, showReturnToCurrentSegment: true),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("PreviousChapterButton")), "上一章");
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("PreviousSegmentButton")), "上一段");
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("PrimaryPlaybackButton")), "播放");
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("NextSegmentButton")), "下一段");
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("NextChapterButton")), "下一章");
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("ReturnToCurrentSegmentButton")), "回到当前段");
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("BackButton")), "返回");

            var primaryIcon = Assert.IsType<SymbolIcon>(FindDescendant<SymbolIcon>(
                Assert.IsType<Button>(view.FindName("PrimaryPlaybackButton")),
                static _ => true));
            var previousChapterIcon = Assert.IsType<SymbolIcon>(FindDescendant<SymbolIcon>(
                Assert.IsType<Button>(view.FindName("PreviousChapterButton")),
                static _ => true));
            var nextChapterIcon = Assert.IsType<SymbolIcon>(FindDescendant<SymbolIcon>(
                Assert.IsType<Button>(view.FindName("NextChapterButton")),
                static _ => true));

            Assert.Equal(SymbolRegular.PlayCircle24, primaryIcon.Symbol);
            Assert.Equal(SymbolRegular.ChevronDoubleLeft20, previousChapterIcon.Symbol);
            Assert.Equal(SymbolRegular.ChevronDoubleRight20, nextChapterIcon.Symbol);
        });
    }

    [Fact]
    public void PlayerView_auto_centering_keeps_current_segment_near_viewport_middle_instead_of_scrolling_to_bottom()
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
                false,
                "魔性沧月",
                true));
            var chapter = new PlaybackChapterContent(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证自动居中不会把列表滚到最底部。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [new PlaybackChapterContent(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsStore(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new FakePlayerAutoScrollCoordinator());

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

                coordinator.Publish(coordinator.CurrentSnapshot with
                {
                    SegmentIndex = 40,
                    SegmentCount = 90
                });

                Pump(TimeSpan.FromMilliseconds(80));
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
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 48d);
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
                false,
                "魔性沧月",
                true));
            var chapter = new PlaybackChapterContent(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证回到当前段不会把列表滚到最底部。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [new PlaybackChapterContent(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null)]),
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
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 48d);
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
                false,
                "魔性沧月",
                true));
            var chapter = new PlaybackChapterContent(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证播放自动切段不会抢回用户滚动位置。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [new PlaybackChapterContent(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null)]),
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
                false,
                "魔性沧月",
                true));
            var chapter = new PlaybackChapterContent(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证主动切换段落后会重新居中。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [new PlaybackChapterContent(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null)]),
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

                viewModel.NextSegmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Pump(TimeSpan.FromMilliseconds(80));
                WaitUntil(() => !view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(1200));
                view.UpdateLayout();
                DoEvents();

                var currentContainer = Assert.IsAssignableFrom<FrameworkElement>(
                    segmentsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem));
                var itemTop = currentContainer.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                var itemCenter = itemTop + (currentContainer.ActualHeight / 2d);
                var viewportCenter = scrollViewer.ViewportHeight / 2d;

                Assert.False(viewModel.ShowReturnToCurrentSegment);
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 48d);
            }
            finally
            {
                window.Close();
            }
        });
    }

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
                false,
                "魔性沧月",
                true));
            var chapter = new PlaybackChapterContent(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证连续跳转会取消旧动画。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [new PlaybackChapterContent(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null)]),
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
                SegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(260)
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
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 48d);
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
                false,
                "魔性沧月",
                true));
            var chapter = new PlaybackChapterContent(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证减少动画时直接定位。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [new PlaybackChapterContent(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null)]),
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
                window.Show();
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
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 48d);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PlayerView_virtualized_target_uses_deferred_realization_and_short_animation()
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
                false,
                "魔性沧月",
                true));
            var chapter = new PlaybackChapterContent(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 120)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证虚拟化目标延迟生成时仍能平滑居中。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [new PlaybackChapterContent(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null)]),
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
                SegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(180)
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
                    SegmentIndex = 88,
                    SegmentCount = 120
                });

                WaitUntil(() => view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(500));
                WaitUntil(() => !view.HasActiveSegmentScrollAnimation, TimeSpan.FromMilliseconds(1200));
                DoEvents();
                view.UpdateLayout();

                var segmentsListBox = Assert.IsType<ListBox>(view.FindName("SegmentListBox"));
                var scrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentsListBox));
                var currentContainer = Assert.IsAssignableFrom<FrameworkElement>(
                    segmentsListBox.ItemContainerGenerator.ContainerFromItem(viewModel.CurrentSegmentItem));
                var itemTop = currentContainer.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                var itemCenter = itemTop + (currentContainer.ActualHeight / 2d);
                var viewportCenter = scrollViewer.ViewportHeight / 2d;

                Assert.Equal(88, viewModel.CurrentSegmentIndex);
                Assert.InRange(Math.Abs(itemCenter - viewportCenter), 0d, 48d);
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
                false,
                "魔性沧月",
                true));
            var chapter = new PlaybackChapterContent(
                0,
                "第三章 来自星空的压力",
                Enumerable.Range(0, 90)
                    .Select(index => new SpeechSegment(index, index * 10, 10, $"第 {index + 1} 段", $"这是第 {index + 1} 段的正文，用来验证页面离开会取消滚动动画。"))
                    .ToArray());
            var viewModel = new PlayerViewModel(
                coordinator,
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-1", "信息全知者", [new PlaybackChapterContent(0, "第三章 来自星空的压力", [])], "魔性沧月"),
                    chapter),
                new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null)]),
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
                SegmentAutoCenterAnimationDuration = TimeSpan.FromMilliseconds(260)
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

            window.Show();
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

    private static FrameworkElement? FindDescendantByContent(DependencyObject root, string content)
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is FrameworkElement element &&
                element is ContentControl contentControl &&
                string.Equals(contentControl.Content as string, content, StringComparison.Ordinal))
            {
                return element;
            }

            var descendant = FindDescendantByContent(child, content);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root, Predicate<T> predicate)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T typedChild && predicate(typedChild))
            {
                return typedChild;
            }

            var descendant = FindDescendant(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static FrameworkElement? FindVisibleDescendantByContent(DependencyObject root, string content)
    {
        var element = FindDescendantByContent(root, content);
        if (element is null || !IsEffectivelyVisible(element, root))
        {
            return null;
        }

        return element;
    }

    private static TextBlock? FindVisibleDescendantByText(DependencyObject root, string text)
    {
        var element = FindDescendant<TextBlock>(
            root,
            textBlock => string.Equals(textBlock.Text, text, StringComparison.Ordinal));
        if (element is null || !IsEffectivelyVisible(element, root))
        {
            return null;
        }

        return element;
    }

    private static bool IsEffectivelyVisible(FrameworkElement element, DependencyObject searchRoot)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement uiElement && uiElement.Visibility != Visibility.Visible)
            {
                return false;
            }

            if (ReferenceEquals(current, searchRoot))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static Rect GetBoundsRelativeToRoot(FrameworkElement element, Visual root)
    {
        var origin = element.TransformToAncestor(root).Transform(new Point(0, 0));
        return new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static void AssertButtonMetadata(Button button, string expectedName)
    {
        Assert.Equal(expectedName, button.ToolTip);
        Assert.Equal(expectedName, AutomationProperties.GetName(button));
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (predicate())
            {
                return;
            }

            DoEvents();
            Thread.Sleep(15);
        }

        DoEvents();
        Assert.True(predicate());
    }

    private static void Pump(TimeSpan duration)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            DoEvents();
            Thread.Sleep(15);
        }
    }

    private sealed class PlayerViewLayoutTestContext
    {
        public PlayerViewLayoutTestContext(
            ObservableCollection<PlayerChapterItemViewModel> chapters,
            ObservableCollection<PlayerSegmentItemViewModel> segments,
            bool showReturnToCurrentSegment = false,
            bool showPlaybackControls = true,
            bool showNoRuleState = false,
            bool showPlaybackErrorBar = false,
            string errorText = "",
            bool showInlineLoadingState = false,
            string inlineLoadingText = "")
        {
            Chapters = chapters;
            Segments = segments;
            CurrentChapterItem = chapters.Count > 10 ? chapters[10] : chapters[0];
            CurrentSegmentItem = segments.Count > 32 ? segments[32] : segments[0];
            ShowReturnToCurrentSegment = showReturnToCurrentSegment;
            ShowPlaybackControls = showPlaybackControls;
            ShowNoRuleState = showNoRuleState;
            ShowPlaybackErrorBar = showPlaybackErrorBar;
            ErrorText = errorText;
            ShowInlineLoadingState = showInlineLoadingState;
            InlineLoadingText = inlineLoadingText;
        }

        public IRelayCommand BackCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ToggleRuleMenuCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ToggleSpeedMenuCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand OpenRuleMenuCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand OpenRulesManagementCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ApplySpeakSpeedCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand IncreaseSpeakSpeedCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand DecreaseSpeakSpeedCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand PreviousChapterCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand PreviousSegmentCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand TogglePlayPauseCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand NextSegmentCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand NextChapterCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand SelectChapterCommand { get; } = new RelayCommand<PlayerChapterItemViewModel?>(_ => { });

        public IRelayCommand SelectSegmentCommand { get; } = new RelayCommand<PlayerSegmentItemViewModel?>(_ => { });

        public IRelayCommand ReturnToCurrentSegmentCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand RetryCurrentSegmentCommand { get; } = new RelayCommand(() => { });

        public string CurrentTitle { get; } = "信息全知者";

        public string CurrentAuthor { get; } = "魔性沧月";

        public string CurrentChapterTitle { get; } = "第二章 头铁的落款";

        public string SpeakSpeedButtonText { get; } = "语速 10";

        public int SpeakSpeed { get; } = 10;

        public SymbolRegular PrimaryActionSymbol { get; } = SymbolRegular.PlayCircle24;

        public string ErrorText { get; }

        public string DisplayedSegmentCounterText { get; } = "33 / 140";

        public string InlineLoadingText { get; }

        public string SpeedEditorText { get; set; } = "10";

        public string SpeedEditorErrorText { get; } = string.Empty;

        public bool IsRuleMenuOpen { get; set; }

        public bool IsSpeedMenuOpen { get; set; }

        public bool ShouldAutoCenterCurrentSegment { get; } = true;

        public bool ShowReturnToCurrentSegment { get; }

        public bool ShowPlaybackControls { get; }

        public bool ShowNoRuleState { get; }

        public bool ShowPlaybackErrorBar { get; }

        public bool ShowInlineLoadingState { get; }

        public bool HasRules { get; } = false;

        public bool HasAvailableRule { get; } = true;

        public bool CanTogglePlayPause { get; } = true;

        public bool CanDecreaseSpeakSpeed { get; } = true;

        public bool CanIncreaseSpeakSpeed { get; } = true;

        public bool CanGoToPreviousChapter { get; } = true;

        public bool CanGoToNextChapter { get; } = true;

        public bool CanGoToPreviousSegment { get; } = true;

        public bool CanGoToNextSegment { get; } = true;

        public string PrimaryActionText { get; } = "播放";

        public double SegmentProgressMaximum { get; } = 139d;

        public double SegmentProgressValue { get; } = 32d;

        public ObservableCollection<PlayerRuleItemViewModel> Rules { get; } = [];

        public ObservableCollection<PlayerChapterItemViewModel> Chapters { get; }

        public ObservableCollection<PlayerSegmentItemViewModel> Segments { get; }

        public PlayerChapterItemViewModel CurrentChapterItem { get; }

        public PlayerSegmentItemViewModel CurrentSegmentItem { get; }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackCoordinator
    {
        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                ChapterTitle = chapterIndex == 0 ? "第一章" : "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }

        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                SegmentIndex = segmentIndex
            });
            return Task.CompletedTask;
        }

        public Task NextSegmentAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                SegmentIndex = CurrentSnapshot.SegmentIndex + 1,
                SegmentCount = Math.Max(CurrentSnapshot.SegmentCount, CurrentSnapshot.SegmentIndex + 2)
            });
            return Task.CompletedTask;
        }

        public Task PreviousSegmentAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                SegmentIndex = Math.Max(CurrentSnapshot.SegmentIndex - 1, 0)
            });
            return Task.CompletedTask;
        }

        public Task NextChapterAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = CurrentSnapshot.ChapterIndex + 1,
                ChapterTitle = "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }

        public Task PreviousChapterAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = Math.Max(CurrentSnapshot.ChapterIndex - 1, 0),
                ChapterTitle = "第一章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SkipCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent? _book;
        private readonly PlaybackChapterContent? _chapter;

        public FakeBookPlaybackContentService(PlaybackBookContent? book, PlaybackChapterContent? chapter)
        {
            _book = book;
            _chapter = chapter;
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            return Task.FromResult(_chapter);
        }
    }

    private sealed class FakeTtsRuleLibraryService : ITtsRuleLibraryService
    {
        private readonly IReadOnlyList<TtsRuleSummary> _rules;

        public FakeTtsRuleLibraryService(IReadOnlyList<TtsRuleSummary> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);
        public Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleImportResult> ImportJsonTextAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> ExportEditorJsonAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsStore : IAppSettingsService
    {
        public FakeAppSettingsStore(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Settings);
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            Settings = (Settings with
            {
                DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? Settings.DefaultSpeakSpeed
            }).Normalize();
            return Task.FromResult(Settings);
        }
    }

    private sealed class FakeAppFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception)
        {
            return new ProjectedUiError(exception.Message, UiMessageSeverity.Error, false);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowInformation(string title, string message)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
        }
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public Wpf.Ui.Controls.INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => false;
        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType) => true;
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;
        public void SetNavigationControl(Wpf.Ui.Controls.INavigationView navigation)
        {
        }
    }

    private sealed class FakePlayerAutoScrollCoordinator : IPlayerAutoScrollCoordinator
    {
        public PlayerAutoScrollState State => PlayerAutoScrollState.AutoCentering;

        public bool ShouldAutoCenter => true;

        public bool ShowReturnToCurrentSegment => false;

        public int PendingRestoreVersion => 0;

        public event EventHandler? StateChanged
        {
            add { }
            remove { }
        }

        public void NotifyUserScrollInput()
        {
        }

        public void BeginScrollbarDrag()
        {
        }

        public void EndScrollbarDrag()
        {
        }

        public void BeginProgrammaticScroll()
        {
        }

        public void EndProgrammaticScroll()
        {
        }

        public void ResumeAutoCenter()
        {
        }

        public void ResetForPageLeave()
        {
        }
    }
}
