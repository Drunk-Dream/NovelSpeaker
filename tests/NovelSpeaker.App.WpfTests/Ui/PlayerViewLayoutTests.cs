using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.StyleGallery;
using Wpf.Ui;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using WpfUiButton = Wpf.Ui.Controls.Button;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed partial class PlayerViewTests
{
    private void PlayerView_explains_empty_chapter_and_disables_segment_playback_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(
                    new ObservableCollection<PlayerChapterItemViewModel> { new(0, "空章节") },
                    [])
            };

            view.Measure(new Size(960, 640));
            view.Arrange(new Rect(0, 0, 960, 640));
            view.UpdateLayout();

            var emptyStatus = Assert.IsType<AppStatusView>(view.FindName("EmptyChapterStatusView"));
            Assert.Equal(Visibility.Visible, emptyStatus.Visibility);
            Assert.Equal("当前章节没有可播放段落", emptyStatus.Title);
            Assert.Equal("0 / 0", Assert.IsType<TextBlock>(FindVisibleDescendantByText(view, "0 / 0")).Text);
            Assert.False(Assert.IsType<WpfUiButton>(view.FindName("PrimaryPlaybackButton")).IsEnabled);
            Assert.False(Assert.IsType<WpfUiButton>(view.FindName("PreviousSegmentButton")).IsEnabled);
            Assert.False(Assert.IsType<WpfUiButton>(view.FindName("NextSegmentButton")).IsEnabled);
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<Grid>(view.FindName("SegmentProgressPanel")).Visibility);
        });
    }

    private void PlayerView_highlights_every_active_cache_selection_and_replaces_current_badge_with_percentage()
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
            Assert.Same(view.FindResource("App.Button.Floating"), firstButton.Style);
            Assert.Same(view.FindResource("App.Button.Floating"), secondButton.Style);
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
            var firstPercentage = Assert.IsType<TextBlock>(FindVisibleDescendantByText(firstItem, "25%"));
            Assert.Same(view.FindResource("App.Brush.Interaction.Foreground.Selected"), firstPercentage.Foreground);
            Assert.Null(VisualTreeTestHelper.FindDescendant<Border>(
                firstItem,
                static border => border.CornerRadius.TopLeft >= 999));
            Assert.Contains("当前章节", AutomationProperties.GetName(firstButton), StringComparison.Ordinal);
            Assert.Contains("已选择缓存", AutomationProperties.GetName(firstButton), StringComparison.Ordinal);
            Assert.Contains("缓存进度 25%", AutomationProperties.GetName(firstButton), StringComparison.Ordinal);
            Assert.Contains("已选择缓存", AutomationProperties.GetName(secondButton), StringComparison.Ordinal);
        });
    }

    private static Border FindChapterCard(DependencyObject item)
    {
        return Assert.IsType<Border>(VisualTreeTestHelper.FindDescendant<Border>(
            item,
            static border =>
                Grid.GetColumn(border) == 1 &&
                border.Child is Grid &&
                border.Padding.Left == 12));
    }

    private void PlayerView_exposes_active_cache_tool_and_selection_actions_with_automation_names()
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

            var toolButton = Assert.IsType<WpfUiButton>(view.FindName("ActiveCacheToolButton"));
            var locateButton = Assert.IsType<Button>(view.FindName("LocateCurrentChapterButton"));
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
            Assert.Equal("定位到当前章节", locateButton.ToolTip);
            Assert.Equal("定位到当前章节", AutomationProperties.GetName(locateButton));
            Assert.Equal(Visibility.Collapsed, locateButton.Visibility);
        });
    }

    private void PlayerView_keeps_catalog_and_segments_scrollable_inside_their_cards()
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

    private void PlayerView_keeps_playback_footer_visible_with_long_content()
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
            var playButton = Assert.IsType<WpfUiButton>(view.FindName("PrimaryPlaybackButton"));

            Assert.Equal(Visibility.Visible, footer.Visibility);
            Assert.True(GetBoundsRelativeToRoot(footer, view).Bottom <= view.ActualHeight);
            Assert.True(GetBoundsRelativeToRoot(playButton, view).Bottom <= view.ActualHeight);
        });
    }

    private void PlayerView_places_book_title_in_toolbar_and_moves_chapter_title_to_preview_header()
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

            var backButton = FindUiButtonByAutomationName(view, "返回");
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

    private void PlayerView_shows_return_to_current_segment_button_when_manual_browsing()
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
            Assert.Equal(new Thickness(0), returnButton.BorderThickness);
            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(returnButton.Background).Color);
            var floatingSurface = VisualTreeTestHelper.FindDescendant<Border>(
                returnButton,
                static border => border.CornerRadius.TopLeft >= 999);
            Assert.NotNull(floatingSurface);
            Assert.Same(view.FindResource("App.Surface.FloatingAction"), floatingSurface!.Style);
        });
    }

    private void PlayerView_replaces_control_area_with_no_rule_state()
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
            var noRuleFooter = Assert.IsType<AppStatusView>(view.FindName("NoRuleStatusView"));
            var backButton = FindUiButtonByAutomationName(view, "返回");

            Assert.Equal(Visibility.Visible, emptyStateButton.Visibility);
            Assert.Equal(Visibility.Visible, noRuleFooter.Visibility);
            Assert.True(GetBoundsRelativeToRoot(noRuleFooter, view).Bottom <= view.ActualHeight);
            Assert.True(GetBoundsRelativeToRoot(backButton, view).Top >= 0);
        });
    }

    private void PlayerView_shows_error_bar_only_when_faulted()
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
                Assert.IsType<WpfUiButton>(faultedView.FindName("ErrorRuleMenuButton")),
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
                Assert.IsType<WpfUiButton>(normalView.FindName("ErrorRuleMenuButton")),
                normalView));
        });
    }

    private void PlayerView_keeps_catalog_visible_at_minimum_supported_width()
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

    private void PlayerView_keeps_catalog_and_preview_cards_at_the_same_height_without_error_bar()
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

            var catalogBorder = Assert.IsType<AppSectionSurface>(view.FindName("CatalogPanelSurface"));
            var previewBorder = Assert.IsType<AppSectionSurface>(view.FindName("PreviewPanelSurface"));

            Assert.InRange(Math.Abs(catalogBorder.ActualHeight - previewBorder.ActualHeight), 0d, 1d);
        });
    }

    private void PlayerView_uses_single_line_truncated_chapter_titles_without_horizontal_scroll()
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

    private void PlayerView_uses_accent_filled_segment_progress_slider()
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
            var accentBrush = Assert.IsType<SolidColorBrush>(System.Windows.Application.Current.FindResource("App.Brush.Accent"));
            var trackBrush = Assert.IsType<SolidColorBrush>(System.Windows.Application.Current.FindResource("App.Brush.Border.Subtle"));
            var fillBarForeground = Assert.IsType<SolidColorBrush>(fillBar.Foreground);
            var fillBarBackground = Assert.IsType<SolidColorBrush>(fillBar.Background);

            Assert.Equal(accentBrush.Color, fillBarForeground.Color);
            Assert.Equal(trackBrush.Color, fillBarBackground.Color);
            Assert.Same(view.FindResource("App.Progress.MediaTrack"), fillBar.Style);
            Assert.Same(view.FindResource("App.Media.ProgressSlider"), slider.Style);
            var renderedTrack = VisualTreeTestHelper.FindDescendant<Border>(
                fillBar,
                border => border.Background is SolidColorBrush brush && brush.Color == trackBrush.Color);
            Assert.NotNull(renderedTrack);
            Assert.True(renderedTrack!.ActualWidth > 0);
            Assert.True(renderedTrack.ActualHeight > 0);
            Assert.Equal(6, fillBar.Height);
            Assert.Equal(6, fillBar.MinHeight);
            Assert.Equal(Colors.Transparent,
                Assert.IsType<SolidColorBrush>(slider.Style.Resources["SliderTrackFill"]).Color);
            Assert.Equal(Colors.Transparent,
                Assert.IsType<SolidColorBrush>(slider.Style.Resources["SliderTrackFillPointerOver"]).Color);
            Assert.Equal(slider.Maximum, fillBar.Maximum);
            Assert.Equal(48d, fillBar.Value);
            Assert.Equal(32d, slider.Value);
            Assert.Equal(
                "SegmentProgressPreviewValue",
                fillBar.GetBindingExpression(RangeBase.ValueProperty)?.ParentBinding.Path.Path);
            Assert.True(fillBar.Value > 0);
            Assert.True(fillBar.Maximum > fillBar.Value);
            var progressToolTip = Assert.IsType<ToolTip>(slider.ToolTip);
            progressToolTip.PlacementTarget = slider;
            DoEvents();
            Assert.Equal("33 / 140", progressToolTip.Content);
            Assert.True(progressToolTip.StaysOpen);
            Assert.False(ToolTipService.GetIsEnabled(slider));
            Assert.NotNull(progressToolTip.GetBindingExpression(ContentControl.ContentProperty));
        });
    }

    private void PlayerView_uses_icon_buttons_with_accessible_metadata_for_playback_controls()
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

            AssertButtonMetadata(Assert.IsType<WpfUiButton>(view.FindName("PreviousChapterButton")), "上一章");
            AssertButtonMetadata(Assert.IsType<WpfUiButton>(view.FindName("PreviousSegmentButton")), "上一段");
            AssertButtonMetadata(Assert.IsType<WpfUiButton>(view.FindName("PrimaryPlaybackButton")), "播放");
            AssertButtonMetadata(Assert.IsType<WpfUiButton>(view.FindName("NextSegmentButton")), "下一段");
            AssertButtonMetadata(Assert.IsType<WpfUiButton>(view.FindName("NextChapterButton")), "下一章");
            var volumeButton = Assert.IsType<WpfUiButton>(view.FindName("VolumeMenuButton"));
            Assert.Equal("播放音量", volumeButton.ToolTip);
            Assert.Equal("播放音量 100%", AutomationProperties.GetName(volumeButton));
            AssertButtonMetadata(Assert.IsType<Button>(view.FindName("ReturnToCurrentSegmentButton")), "返回当前段落");
            AssertButtonMetadata(FindUiButtonByAutomationName(view, "返回"), "返回");
            Assert.Null(view.FindName("SkipCurrentSegmentButton"));

            var primaryIcon = Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(
                Assert.IsType<WpfUiButton>(view.FindName("PrimaryPlaybackButton")),
                static _ => true));
            var previousChapterIcon = Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(
                Assert.IsType<WpfUiButton>(view.FindName("PreviousChapterButton")),
                static _ => true));
            var nextChapterIcon = Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(
                Assert.IsType<WpfUiButton>(view.FindName("NextChapterButton")),
                static _ => true));

            Assert.Equal(SymbolRegular.PlayCircle24, primaryIcon.Symbol);
            Assert.Equal(SymbolRegular.ChevronDoubleLeft20, previousChapterIcon.Symbol);
            Assert.Equal(SymbolRegular.ChevronDoubleRight20, nextChapterIcon.Symbol);

            Assert.IsType<Grid>(view.FindName("PlaybackControlBar"));
            var mediaControls = Assert.IsType<WrapPanel>(view.FindName("PlaybackMediaControls"));
            Assert.Equal(1, Grid.GetColumn(mediaControls));
            Assert.Equal(HorizontalAlignment.Center, mediaControls.HorizontalAlignment);
            Assert.Equal(2, Grid.GetColumn(volumeButton));
            Assert.Equal(HorizontalAlignment.Right, volumeButton.HorizontalAlignment);

            var mediaButtonStyle = view.FindResource("App.Media.Button");
            var mediaButtons = new[]
            {
                Assert.IsType<WpfUiButton>(view.FindName("PreviousChapterButton")),
                Assert.IsType<WpfUiButton>(view.FindName("PreviousSegmentButton")),
                Assert.IsType<WpfUiButton>(view.FindName("PrimaryPlaybackButton")),
                Assert.IsType<WpfUiButton>(view.FindName("NextSegmentButton")),
                Assert.IsType<WpfUiButton>(view.FindName("NextChapterButton")),
                volumeButton
            };
            Assert.All(mediaButtons, button =>
            {
                Assert.Same(mediaButtonStyle, button.Style);
                Assert.Equal(48, button.Width);
                Assert.Equal(48, button.Height);
                Assert.Equal(20, button.FontSize);
                Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(button.Background).Color);
            });

            var volumePopup = Assert.IsType<Wpf.Ui.Controls.Flyout>(view.FindName("VolumeFlyout"));
            var volumeSlider = Assert.IsType<Slider>(view.FindName("VolumeSlider"));
            Assert.False(volumePopup.IsOpen);
            Assert.Equal(0d, volumeSlider.Minimum);
            Assert.Equal(1d, volumeSlider.Maximum);
            Assert.Equal(Orientation.Vertical, volumeSlider.Orientation);
            Assert.Equal(32, volumeSlider.Width);
            Assert.Equal(160, volumeSlider.Height);
            Assert.Equal("播放音量", AutomationProperties.GetName(volumeSlider));
            Assert.Same(view.FindResource("App.Media.VolumeSlider"), volumeSlider.Style);
            Assert.Null(VisualTreeTestHelper.FindDescendant<TextBlock>(
                volumePopup,
                textBlock => textBlock.Text == "仅调整应用内播放音量，不改变系统音量。"));
        });
    }

    [Fact]
    public void Player_view_popups_render_one_shared_surface_at_runtime()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new PlayerView
            {
                DataContext = CreateDefaultVisualContext()
            };
            var window = new Window
            {
                Content = view,
                Width = 1280,
                Height = 760,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            using var host = WpfWindowHost.Show(window);
            window.UpdateLayout();

            var popupHostStyle = Assert.IsType<Style>(view.FindResource("App.Feedback.PopupHost"));
            var popupSurfaceStyle = Assert.IsType<Style>(view.FindResource("App.Feedback.PopupSurface"));
            foreach (var popupName in new[] { "RuleMenuPopup", "SpeedMenuPopup" })
            {
                var popup = Assert.IsType<Popup>(view.FindName(popupName));
                Assert.Same(popupHostStyle, popup.Style);
                Assert.True(popup.AllowsTransparency);
                Assert.False(popup.Focusable);

                var opened = false;
                popup.Opened += (_, _) => opened = true;
                popup.IsOpen = true;
                Assert.True(opened);
                popup.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });
                var popupChild = Assert.IsAssignableFrom<DependencyObject>(popup.Child);
                var popupLayer = Assert.Single(TransientPopupVisualRenderer.CaptureOpenHostLayers(view, 96));
                AssertRoundedSurfaceCorners(popupLayer.Bitmap);
                var borders = (popupChild is Border rootBorder
                        ? new[] { rootBorder }
                        : Enumerable.Empty<Border>())
                    .Concat(VisualTreeTestHelper.FindDescendants<Border>(popupChild))
                    .ToArray();
                var surfaces = borders
                    .Where(border => ReferenceEquals(border.Style, popupSurfaceStyle))
                    .ToArray();
                Assert.Single(surfaces);
                Assert.Null(surfaces[0].Effect);
                Assert.All(
                    FindVisualAncestors(surfaces[0]).OfType<Border>(),
                    border =>
                    {
                        Assert.True(IsTransparent(border.Background));
                        Assert.True(IsTransparent(border.BorderBrush));
                        Assert.Null(border.Effect);
                    });

                popup.IsOpen = false;
            }

            foreach (var flyoutName in new[] { "StopTimerFlyout", "VolumeFlyout" })
            {
                var flyout = Assert.IsType<Wpf.Ui.Controls.Flyout>(view.FindName(flyoutName));
                flyout.ApplyTemplate();
                var flyoutPopup = Assert.IsType<Popup>(flyout.Template.FindName("PART_Popup", flyout));
                Assert.True(flyoutPopup.AllowsTransparency);
                Assert.Null(flyout.Effect);
                Assert.True(IsTransparent(flyout.Background));
                Assert.True(IsTransparent(flyout.BorderBrush));
                Assert.Equal(0, flyout.BorderThickness.Left);
                flyout.IsOpen = true;
                flyout.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });
                var flyoutLayer = Assert.Single(TransientPopupVisualRenderer.CaptureOpenHostLayers(view, 96));
                AssertRoundedSurfaceCorners(flyoutLayer.Bitmap);
                flyout.IsOpen = false;
            }
        });

        static IEnumerable<DependencyObject> FindVisualAncestors(DependencyObject element)
        {
            var current = VisualTreeHelper.GetParent(element);
            while (current is not null)
            {
                yield return current;
                current = VisualTreeHelper.GetParent(current);
            }
        }

        static bool IsTransparent(Brush? brush) =>
            brush is null || brush is SolidColorBrush { Color.A: 0 };

    }

    private void Player_visual_review_generates_stable_page_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var scenarios = new[]
            {
                new PageVisualReviewScenario("default", 1d, page => ConfigurePlayerPage(page, CreateDefaultVisualContext())),
                new PageVisualReviewScenario("default", 1.25d, page => ConfigurePlayerPage(page, CreateDefaultVisualContext())),
                new PageVisualReviewScenario("default", 1.5d, page => ConfigurePlayerPage(page, CreateDefaultVisualContext())),
                new PageVisualReviewScenario("long-body", 1.5d, page => ConfigurePlayerPage(page, CreateLongVisualContext())),
                new PageVisualReviewScenario(
                    "empty-chapter",
                    1d,
                    page => ConfigurePlayerPage(
                        page,
                        new PlayerViewLayoutTestContext(
                            new ObservableCollection<PlayerChapterItemViewModel> { new(0, "空章节") },
                            []))),
                new PageVisualReviewScenario(
                    "error",
                    1d,
                    page => ConfigurePlayerPage(
                        page,
                        new PlayerViewLayoutTestContext(
                            new ObservableCollection<PlayerChapterItemViewModel> { new(0, "第一章") },
                            new ObservableCollection<PlayerSegmentItemViewModel> { new(0, 0, "用于错误场景的脱敏正文。") },
                            showPlaybackErrorBar: true,
                            errorText: "播放暂时不可用，请稍后重试。"))),
                new PageVisualReviewScenario(
                    "no-rule",
                    1d,
                    page => ConfigurePlayerPage(
                        page,
                        new PlayerViewLayoutTestContext(
                            new ObservableCollection<PlayerChapterItemViewModel> { new(0, "第一章") },
                            [],
                            showNoRuleState: true))),
                new PageVisualReviewScenario(
                    "stop-timer-flyout",
                    1d,
                    page =>
                    {
                        var context = CreateDefaultVisualContext();
                        ConfigurePlayerPage(page, context);
                    },
                    true,
                    page =>
                    {
                        var playerView = Assert.IsType<PlayerView>(page.FindName("PlayerView"));
                        Assert.IsType<Wpf.Ui.Controls.Flyout>(
                            playerView.FindName("StopTimerFlyout")).IsOpen = true;
                    }),
                new PageVisualReviewScenario(
                    "volume-flyout",
                    1d,
                    page =>
                    {
                        var context = CreateDefaultVisualContext();
                        ConfigurePlayerPage(page, context);
                    },
                    true,
                    page =>
                    {
                        var playerView = Assert.IsType<PlayerView>(page.FindName("PlayerView"));
                        Assert.IsType<Wpf.Ui.Controls.Flyout>(
                            playerView.FindName("VolumeFlyout")).IsOpen = true;
                    })
            };

            PageVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "player",
                scenarios,
                CreateVisualReviewPage);
        });
    }

    private void Player_feedback_visual_review_generates_popup_and_volume_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var outputDirectory = Path.Combine(
                LocateRepositoryRoot(),
                "artifacts",
                "visual-review",
                "player-feedback");
            Directory.CreateDirectory(outputDirectory);
            var size = new Size(960, 640);

            try
            {
                GalleryThemeRuntime.EnsureProviderResources();
                foreach (var (themeName, applyTheme) in new (string Name, Action Apply)[]
                         {
                             ("light", () => GalleryThemeRuntime.Apply(GalleryTheme.Light)),
                             ("dark", () => GalleryThemeRuntime.Apply(GalleryTheme.Dark))
                         })
                {
                    applyTheme();
                    using var fixture = CreateVisualReviewPage();
                    ConfigurePlayerPage(fixture.Page, CreateDefaultVisualContext());
                    using var controlHost = new WpfControlHost(fixture.Page);
                    controlHost.MeasureArrange(size);
                    var playerView = Assert.IsType<PlayerView>(fixture.Page.FindName("PlayerView"));
                    var window = new Window
                    {
                        Width = size.Width,
                        Height = size.Height,
                        Content = fixture.Page,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize
                    };
                    using var windowHost = WpfWindowHost.Show(window);
                    window.UpdateLayout();

                    foreach (var popupName in new[]
                             {
                                 "RuleMenuPopup",
                                 "SpeedMenuPopup",
                                 "StopTimerFlyout",
                                 "VolumeFlyout"
                             })
                    {
                        var setOpen = playerView.FindName(popupName) switch
                        {
                            Popup ruleOrSpeed => (Action<bool>)(value => ruleOrSpeed.IsOpen = value),
                            Wpf.Ui.Controls.Flyout flyout => value => flyout.IsOpen = value,
                            _ => throw new InvalidOperationException($"Popup '{popupName}' was not found.")
                        };
                        setOpen(true);
                        fixture.Page.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });
                        var hostLayer = Assert.Single(
                            TransientPopupVisualRenderer.CaptureOpenHostLayers(fixture.Page, 96));
                        AssertRoundedSurfaceCorners(hostLayer.Bitmap);
                        var layer = Assert.Single(
                            TransientPopupVisualRenderer.CaptureOpenLayers(fixture.Page, 96));
                        AssertRoundedSurfaceCorners(layer.Bitmap);
                        SavePng(
                            RenderPopupOnCanvas(layer.Bitmap),
                            Path.Combine(outputDirectory, $"player-feedback.{popupName}.{themeName}.png"));
                        setOpen(false);
                    }

                    window.Content = null;
                }
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });

        static BitmapSource RenderPopupOnCanvas(BitmapSource popup)
        {
            var canvas = new Border
            {
                Width = 400,
                Height = 400,
                Child = new Image
                {
                    Width = popup.PixelWidth,
                    Height = popup.PixelHeight,
                    Source = popup,
                    Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            canvas.SetResourceReference(Border.BackgroundProperty, "App.Brush.Canvas");
            using var host = new WpfControlHost(canvas);
            return host.Render(new Size(400, 400), 96);
        }

        static void SavePng(BitmapSource bitmap, string path)
        {
            using var stream = File.Create(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }
    }

    private static void AssertRoundedSurfaceCorners(BitmapSource bitmap)
    {
        Assert.True(bitmap.PixelWidth >= 8);
        Assert.True(bitmap.PixelHeight >= 8);
        foreach (var (x, y) in new[]
                 {
                     (0, 0),
                     (1, 0),
                     (0, 1),
                     (bitmap.PixelWidth - 1, 0),
                     (bitmap.PixelWidth - 2, 0),
                     (bitmap.PixelWidth - 1, 1),
                     (0, bitmap.PixelHeight - 1),
                     (1, bitmap.PixelHeight - 1),
                     (0, bitmap.PixelHeight - 2),
                     (bitmap.PixelWidth - 1, bitmap.PixelHeight - 1),
                     (bitmap.PixelWidth - 2, bitmap.PixelHeight - 1),
                     (bitmap.PixelWidth - 1, bitmap.PixelHeight - 2)
                 })
        {
            var pixels = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
            var pixel = Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
            Assert.True(
                pixel.A == 0,
                $"Rounded popup corner ({x},{y}) was {pixel} in {bitmap.PixelWidth}x{bitmap.PixelHeight} host capture.");
        }
    }

    [Fact]
    public void Player_view_content_contracts_cover_empty_cache_scroll_and_titles()
    {
        PlayerView_explains_empty_chapter_and_disables_segment_playback_controls();
        PlayerView_highlights_every_active_cache_selection_and_replaces_current_badge_with_percentage();
        PlayerView_exposes_active_cache_tool_and_selection_actions_with_automation_names();
        PlayerView_keeps_catalog_and_segments_scrollable_inside_their_cards();
        PlayerView_places_book_title_in_toolbar_and_moves_chapter_title_to_preview_header();
        PlayerView_replaces_control_area_with_no_rule_state();
    }

    [Fact]
    public void Player_view_geometry_contracts_cover_footer_width_height_and_truncation()
    {
        PlayerView_keeps_playback_footer_visible_with_long_content();
        PlayerView_keeps_catalog_visible_at_minimum_supported_width();
        PlayerView_keeps_catalog_and_preview_cards_at_the_same_height_without_error_bar();
        PlayerView_uses_single_line_truncated_chapter_titles_without_horizontal_scroll();
    }

    [Fact]
    public void Player_view_playback_feedback_contracts_cover_error_progress_and_accessibility()
    {
        PlayerView_shows_return_to_current_segment_button_when_manual_browsing();
        PlayerView_shows_error_bar_only_when_faulted();
        PlayerView_uses_accent_filled_segment_progress_slider();
        PlayerView_uses_icon_buttons_with_accessible_metadata_for_playback_controls();
    }

    [Fact]
    public void Player_view_visual_review_contract_remains_repeatable()
    {
        Player_visual_review_generates_stable_page_screenshots();
    }

    [Fact]
    public void Player_feedback_visual_review_contract_covers_popup_corners_and_volume_rail()
    {
        Player_view_popups_render_one_shared_surface_at_runtime();
        Player_feedback_visual_review_generates_popup_and_volume_screenshots();
    }

    private static PlayerViewLayoutTestContext CreateDefaultVisualContext()
    {
        var chapters = new ObservableCollection<PlayerChapterItemViewModel>();
        for (var index = 0; index < 18; index++)
        {
            chapters.Add(new PlayerChapterItemViewModel(index, $"第 {index + 1} 章 示例章节")
            {
                IsCurrent = index == 4,
                CachePercentageText = index % 3 == 0 ? "75%" : string.Empty
            });
        }

        var segments = new ObservableCollection<PlayerSegmentItemViewModel>();
        for (var index = 0; index < 24; index++)
        {
            segments.Add(new PlayerSegmentItemViewModel(4, index, $"这是用于播放页视觉回归的第 {index + 1} 段脱敏正文。")
            {
                IsCurrent = index == 2,
                VisualOpacity = index == 2 ? 1d : 0.52d
            });
        }

        return new PlayerViewLayoutTestContext(chapters, segments);
    }

    private static PlayerViewLayoutTestContext CreateLongVisualContext()
    {
        var chapters = new ObservableCollection<PlayerChapterItemViewModel>();
        for (var index = 0; index < 60; index++)
        {
            chapters.Add(new PlayerChapterItemViewModel(
                index,
                $"第 {index + 1} 章 这是用于验证长标题截断和目录滚动的章节名称")
            {
                IsCurrent = index == 10
            });
        }

        var segments = new ObservableCollection<PlayerSegmentItemViewModel>();
        for (var index = 0; index < 100; index++)
        {
            segments.Add(new PlayerSegmentItemViewModel(
                10,
                index,
                $"这是第 {index + 1} 段较长的脱敏正文，用于验证播放页在长内容下仍保持正文滚动、当前段轻量高亮和媒体控制可见。")
            {
                IsCurrent = index == 2,
                VisualOpacity = index == 2 ? 1d : 0.52d
            });
        }

        return new PlayerViewLayoutTestContext(chapters, segments);
    }

    private static void ConfigurePlayerPage(FrameworkElement page, PlayerViewLayoutTestContext context)
    {
        Assert.IsType<PlayerPage>(page);
        Assert.IsType<PlayerView>(page.FindName("PlayerView")).DataContext = context;
    }

    private static PageVisualReviewPage CreateVisualReviewPage()
    {
        var provider = WpfTestHost.BuildServiceProvider();
        return new PageVisualReviewPage(
            provider.GetRequiredService<PlayerPage>(),
            () => provider.DisposeAsync().AsTask().GetAwaiter().GetResult());
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

}
