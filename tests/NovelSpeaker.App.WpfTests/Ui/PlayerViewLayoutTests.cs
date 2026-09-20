using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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

    private void PlayerView_exposes_active_cache_tool_and_selection_actions_with_automation_names()
    {
        WpfTestHost.RunInSta(() =>
        {
            var inactiveView = new PlayerView
            {
                DataContext = new PlayerViewLayoutTestContext(
                    new ObservableCollection<PlayerChapterItemViewModel> { new(0, "第一章") },
                    new ObservableCollection<PlayerSegmentItemViewModel> { new(0, 0, "第一段") })
            };
            inactiveView.Measure(new Size(1280, 760));
            inactiveView.Arrange(new Rect(0, 0, 1280, 760));
            inactiveView.UpdateLayout();

            var inactiveToolButton = Assert.IsType<WpfUiButton>(inactiveView.FindName("ActiveCacheToolButton"));
            Assert.Equal("缓存章节", inactiveToolButton.ToolTip);
            Assert.Equal("缓存章节", AutomationProperties.GetName(inactiveToolButton));
            Assert.Equal(SymbolRegular.ArrowDownload24, Assert.IsType<SymbolIcon>(inactiveToolButton.Icon).Symbol);

            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章", isSelectedForActiveCache: true),
                new(1, "第二章", isSelectedForActiveCache: true)
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
            var locateButton = Assert.IsType<WpfUiButton>(view.FindName("LocateCurrentChapterButton"));
            var selectionToolbar = Assert.IsAssignableFrom<FrameworkElement>(view.FindName("ActiveCacheSelectionToolbar"));
            var startButton = Assert.IsType<WpfUiButton>(view.FindName("StartActiveCacheButton"));

            Assert.Equal("退出选择", toolButton.ToolTip);
            Assert.Equal("退出选择", AutomationProperties.GetName(toolButton));
            Assert.Equal(SymbolRegular.Dismiss24, Assert.IsType<SymbolIcon>(toolButton.Icon).Symbol);
            Assert.Equal(Visibility.Visible, selectionToolbar.Visibility);
            Assert.NotNull(FindVisibleDescendantByText(selectionToolbar, "已选择 2 章"));
            Assert.NotNull(FindVisibleDescendantByText(selectionToolbar, "已有主动缓存批次正在运行，完成或取消后可开始新批次。"));
            Assert.Equal("开始缓存", AutomationProperties.GetName(startButton));
            Assert.Null(startButton.Content);
            Assert.Equal(SymbolRegular.ArrowDownload24, Assert.IsType<SymbolIcon>(startButton.Icon).Symbol);
            Assert.False(startButton.IsEnabled);
            Assert.Equal("定位到当前章节", locateButton.ToolTip);
            Assert.Equal("定位到当前章节", AutomationProperties.GetName(locateButton));
            Assert.Equal(Visibility.Collapsed, locateButton.Visibility);
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

            var returnButton = Assert.IsType<WpfUiButton>(view.FindName("ReturnToCurrentSegmentButton"));
            Assert.Equal(Visibility.Visible, returnButton.Visibility);
            Assert.Equal("返回当前段落", returnButton.ToolTip);
            Assert.Equal("返回当前段落", AutomationProperties.GetName(returnButton));
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

            var emptyStateButton = Assert.IsType<WpfUiButton>(FindVisibleDescendantByContent(view, "前往 TTS 规则"));
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

    private void PlayerView_uses_icon_buttons_with_accessible_metadata_for_playback_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章", isCurrent: true)
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
            AssertButtonMetadata(Assert.IsType<WpfUiButton>(view.FindName("ReturnToCurrentSegmentButton")), "返回当前段落");
            AssertButtonMetadata(FindUiButtonByAutomationName(view, "返回"), "返回");
            Assert.Null(view.FindName("SkipCurrentSegmentButton"));

            var volumeSlider = Assert.IsType<Slider>(view.FindName("VolumeSlider"));
            Assert.Equal("播放音量", AutomationProperties.GetName(volumeSlider));
        });
    }

    [Fact]
    public void Player_view_popups_render_one_shared_surface_at_runtime()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new PlayerView { DataContext = CreateDefaultVisualContext() };
            var window = new Window { Content = view, Width = 1280, Height = 760, ShowInTaskbar = false };
            using var host = WpfWindowHost.Show(window);
            var surfaceStyle = Assert.IsType<Style>(view.FindResource("App.Feedback.PopupSurface"));

            foreach (var popupName in new[] { "RuleMenuPopup", "SpeedMenuPopup" })
            {
                var popup = Assert.IsType<Popup>(view.FindName(popupName));
                popup.IsOpen = true;
                popup.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });
                AssertSingleSurface(Assert.IsAssignableFrom<DependencyObject>(popup.Child), surfaceStyle);
                popup.IsOpen = false;
            }

            foreach (var flyoutName in new[] { "StopTimerFlyout", "VolumeFlyout" })
            {
                var flyout = Assert.IsType<Wpf.Ui.Controls.Flyout>(view.FindName(flyoutName));
                flyout.ApplyTemplate();
                var popup = Assert.IsType<Popup>(flyout.Template.FindName("PART_Popup", flyout));
                flyout.IsOpen = true;
                flyout.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });
                AssertSingleSurface(Assert.IsAssignableFrom<DependencyObject>(popup.Child), surfaceStyle);
                flyout.IsOpen = false;
            }
        });
    }

    private static void AssertSingleSurface(DependencyObject root, Style surfaceStyle)
    {
        var borders = (root is Border rootBorder ? new[] { rootBorder } : [])
            .Concat(VisualTreeTestHelper.FindDescendants<Border>(root))
            .ToArray();
        var surface = Assert.Single(borders, border => ReferenceEquals(border.Style, surfaceStyle));
        var fullOpaqueSurfaces = borders.Where(border =>
            border.ActualWidth >= surface.ActualWidth * 0.9 &&
            border.ActualHeight >= surface.ActualHeight * 0.9 &&
            !IsTransparent(border.Background));
        Assert.Equal(surface, Assert.Single(fullOpaqueSurfaces));

        for (var ancestor = VisualTreeHelper.GetParent(surface);
             ancestor is not null;
             ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is Border border)
            {
                Assert.True(IsTransparent(border.Background));
                Assert.True(IsTransparent(border.BorderBrush));
                Assert.Null(border.Effect);
            }
        }
    }

    private static bool IsTransparent(Brush? brush) =>
        brush is null || brush is SolidColorBrush { Color.A: 0 };

    [Fact]
    public void Player_view_content_contracts_cover_empty_cache_scroll_and_titles()
    {
        PlayerView_explains_empty_chapter_and_disables_segment_playback_controls();
        PlayerView_exposes_active_cache_tool_and_selection_actions_with_automation_names();
        PlayerView_replaces_control_area_with_no_rule_state();
    }

    [Fact]
    public void Player_view_playback_feedback_contracts_cover_error_progress_and_accessibility()
    {
        PlayerView_shows_return_to_current_segment_button_when_manual_browsing();
        PlayerView_shows_error_bar_only_when_faulted();
        PlayerView_uses_icon_buttons_with_accessible_metadata_for_playback_controls();
    }

    private static PlayerViewLayoutTestContext CreateDefaultVisualContext()
    {
        var chapters = new ObservableCollection<PlayerChapterItemViewModel>();
        for (var index = 0; index < 18; index++)
        {
            chapters.Add(new PlayerChapterItemViewModel(
                index,
                $"第 {index + 1} 章 示例章节",
                isCurrent: index == 4,
                cachePercentageText: index % 3 == 0 ? "75%" : string.Empty));
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

}
