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

[Collection("WpfDispatcher")]
public sealed partial class PlayerViewTests
{
    [Fact]
    public void PlayerView_highlights_every_active_cache_selection_and_replaces_current_badge_with_percentage()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章")
                {
                    IsCurrent = true,
                    IsSelectedForActiveCache = true,
                    CachePercentageText = "25%"
                },
                new(1, "第二章")
                {
                    IsSelectedForActiveCache = true,
                    CachePercentageText = "50%"
                },
                new(2, "第三章")
            };
            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(
                    chapters,
                    new ObservableCollection<PlayerSegmentItemViewModel>
                    {
                        new(0, 0, "第一段")
                    },
                    isActiveCacheSelectionMode: true)
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var listBox = Assert.IsType<ListBox>(view.FindName("WideChaptersListBox"));
            var firstItem = Assert.IsType<ListBoxItem>(listBox.ItemContainerGenerator.ContainerFromIndex(0));
            var secondItem = Assert.IsType<ListBoxItem>(listBox.ItemContainerGenerator.ContainerFromIndex(1));
            var thirdItem = Assert.IsType<ListBoxItem>(listBox.ItemContainerGenerator.ContainerFromIndex(2));
            var firstCard = FindChapterCard(firstItem);
            var secondCard = FindChapterCard(secondItem);
            var thirdCard = FindChapterCard(thirdItem);
            var firstButton = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(firstItem));
            var secondButton = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(secondItem));
            var currentAccent = VisualTreeTestHelper.FindDescendant<Border>(
                firstItem,
                static border => Grid.GetColumn(border) == 0 &&
                                 border.Child is null &&
                                 border.Opacity == 1);

            Assert.NotEqual(Brushes.Transparent, firstCard.Background);
            Assert.NotEqual(Brushes.Transparent, secondCard.Background);
            Assert.NotEqual(Brushes.Transparent, firstCard.BorderBrush);
            Assert.NotEqual(Brushes.Transparent, secondCard.BorderBrush);
            Assert.Equal(Brushes.Transparent, thirdCard.Background);
            Assert.Equal(Brushes.Transparent, thirdCard.BorderBrush);
            Assert.NotNull(currentAccent);
            Assert.Null(FindVisibleDescendantByText(listBox, "当前"));
            Assert.NotNull(FindVisibleDescendantByText(firstItem, "25%"));
            Assert.NotNull(FindVisibleDescendantByText(secondItem, "50%"));
            Assert.Contains("当前章节", AutomationProperties.GetName(firstButton), StringComparison.Ordinal);
            Assert.Contains("已选择缓存", AutomationProperties.GetName(firstButton), StringComparison.Ordinal);
            Assert.Contains("缓存进度 25%", AutomationProperties.GetName(firstButton), StringComparison.Ordinal);
            Assert.Contains("已选择缓存", AutomationProperties.GetName(secondButton), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PlayerView_exposes_active_cache_tool_and_selection_actions_with_automation_names()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章") { IsSelectedForActiveCache = true },
                new(1, "第二章") { IsSelectedForActiveCache = true }
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
                    isActiveCacheSelectionMode: true,
                    canStartActiveCache: false,
                    activeCacheStatusText: "已有主动缓存批次正在运行，完成或取消后可开始新批次。")
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var toolButton = Assert.IsType<Button>(view.FindName("ActiveCacheToolButton"));
            var selectionToolbar = Assert.IsType<StackPanel>(view.FindName("ActiveCacheSelectionToolbar"));
            var cancelButton = Assert.IsType<Button>(view.FindName("CancelActiveCacheSelectionButton"));
            var startButton = Assert.IsType<Button>(view.FindName("StartActiveCacheButton"));

            Assert.Equal("主动缓存章节", toolButton.ToolTip);
            Assert.Equal("主动缓存章节", AutomationProperties.GetName(toolButton));
            Assert.Equal(Visibility.Visible, selectionToolbar.Visibility);
            Assert.NotNull(FindVisibleDescendantByText(selectionToolbar, "已选择 2 章"));
            Assert.NotNull(FindVisibleDescendantByText(selectionToolbar, "已有主动缓存批次正在运行，完成或取消后可开始新批次。"));
            Assert.Equal("取消选择", AutomationProperties.GetName(cancelButton));
            Assert.Equal("开始缓存", AutomationProperties.GetName(startButton));
            Assert.False(startButton.IsEnabled);
        });
    }

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
            Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(chaptersListBox));

            var chaptersScrollViewer = VisualTreeTestHelper.FindDescendant<ScrollViewer>(chaptersListBox);
            var segmentsScrollViewer = VisualTreeTestHelper.FindDescendant<ScrollViewer>(segmentListBox);

            Assert.NotNull(chaptersScrollViewer);
            Assert.NotNull(segmentsScrollViewer);
            Assert.True(chaptersScrollViewer!.ScrollableHeight > 0);
            Assert.True(segmentsScrollViewer!.ScrollableHeight > 0);
        });
    }

    private static Border FindChapterCard(DependencyObject item)
    {
        return Assert.IsType<Border>(VisualTreeTestHelper.FindDescendant<Border>(
            item,
            static border =>
                Grid.GetColumn(border) == 1 &&
                border.Child is Grid &&
                border.Padding.Left == 12 &&
                border.Padding.Top == 8));
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
            Assert.Equal("返回当前段落", returnButton.ToolTip);
            Assert.Equal("返回当前段落", AutomationProperties.GetName(returnButton));
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
            Assert.True(IsEffectivelyVisible(
                Assert.IsType<Button>(faultedView.FindName("ErrorRuleMenuButton")),
                faultedView));

            var normalView = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(chapters, segments),
            };

            normalView.Measure(new Size(1280, 760));
            normalView.Arrange(new Rect(0, 0, 1280, 760));
            normalView.UpdateLayout();

            Assert.Null(FindVisibleDescendantByContent(normalView, "再次尝试"));
            Assert.False(IsEffectivelyVisible(
                Assert.IsType<Button>(normalView.FindName("ErrorRuleMenuButton")),
                normalView));
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
            var titleText = VisualTreeTestHelper.FindDescendant<TextBlock>(
                itemContainer,
                static textBlock => textBlock.Text.StartsWith("第一章", StringComparison.Ordinal));
            var chapterButton = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(itemContainer));

            Assert.NotNull(titleText);
            Assert.Equal(chapters[0].Title, chapterButton.ToolTip);
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
            var segmentButton = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(itemContainer));

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
            var stopTimerButton = Assert.IsType<Button>(view.FindName("StopTimerToolButton"));
            var speedButton = Assert.IsType<Button>(view.FindName("SpeedMenuButton"));
            var stopTimerPill = Assert.IsType<Border>(view.FindName("StopTimerPillBorder"));
            var speedPill = Assert.IsType<Border>(view.FindName("SpeedMenuPillBorder"));

            Assert.InRange(Math.Abs(ruleButton.ActualHeight - speedButton.ActualHeight), 0d, 1d);
            Assert.InRange(Math.Abs(stopTimerButton.ActualHeight - speedButton.ActualHeight), 0d, 1d);
            Assert.Equal("定时停止", stopTimerButton.ToolTip);
            Assert.Equal(80d, speedPill.ActualWidth);
            Assert.Equal(40d, speedPill.ActualHeight);
            Assert.Equal(new CornerRadius(12), speedPill.CornerRadius);
            Assert.Equal(80d, stopTimerPill.ActualWidth);
            Assert.Equal(40d, stopTimerPill.ActualHeight);
            Assert.Equal(new CornerRadius(12), stopTimerPill.CornerRadius);
        });
    }

    [Fact]
    public void PlayerView_uses_opaque_surfaces_for_rule_and_speed_popups()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(
                    [new PlayerChapterItemViewModel(0, "第一章")],
                    [new PlayerSegmentItemViewModel(0, 0, "第一段")])
            };

            var rulePopup = Assert.IsType<Popup>(view.FindName("RuleMenuPopup"));
            var speedPopup = Assert.IsType<Popup>(view.FindName("SpeedMenuPopup"));

            Assert.True(rulePopup.AllowsTransparency);
            Assert.True(speedPopup.AllowsTransparency);
            Assert.NotEqual(Brushes.Transparent, Assert.IsType<Border>(rulePopup.Child).Background);
            Assert.NotEqual(Brushes.Transparent, Assert.IsType<Border>(speedPopup.Child).Background);
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
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("ReturnToCurrentSegmentButton")), "返回当前段落");
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("BackButton")), "返回");
            Assert.Null(view.FindName("SkipCurrentSegmentButton"));

            var primaryIcon = Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(
                Assert.IsType<Button>(view.FindName("PrimaryPlaybackButton")),
                static _ => true));
            var previousChapterIcon = Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(
                Assert.IsType<Button>(view.FindName("PreviousChapterButton")),
                static _ => true));
            var nextChapterIcon = Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(
                Assert.IsType<Button>(view.FindName("NextChapterButton")),
                static _ => true));

            Assert.Equal(SymbolRegular.PlayCircle24, primaryIcon.Symbol);
            Assert.Equal(SymbolRegular.ChevronDoubleLeft20, previousChapterIcon.Symbol);
            Assert.Equal(SymbolRegular.ChevronDoubleRight20, nextChapterIcon.Symbol);
        });
    }

}
