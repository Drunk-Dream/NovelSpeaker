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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Speech;
using NovelSpeaker.StyleGallery;
using Wpf.Ui;
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

            Assert.NotNull(FindVisibleDescendantByText(view, "当前章节没有可播放段落"));
            Assert.NotNull(FindVisibleDescendantByText(view, "0 / 0"));
            Assert.False(FindUiButtonByAutomationName(view, "播放").IsEnabled);
            Assert.False(FindUiButtonByAutomationName(view, "上一段").IsEnabled);
            Assert.False(FindUiButtonByAutomationName(view, "下一段").IsEnabled);
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

            var inactiveToolButton = FindUiButtonByAutomationName(inactiveView, "章节管理");
            Assert.Equal("章节管理", inactiveToolButton.ToolTip);

            var chapters = new ObservableCollection<PlayerChapterItemViewModel>
            {
                new(0, "第一章", isSelected: true),
                new(1, "第二章", isSelected: true)
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
                    isChapterManagementMode: true,
                    canStartActiveCache: false,
                    activeCacheStatusText: "已有主动缓存批次正在运行，完成或取消后可开始新批次。")
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var toolButton = FindUiButtonByAutomationName(view, "退出选择");
            var locateButton = Assert.IsType<WpfUiButton>(view.FindName("LocateCurrentChapterButton"));
            var startButton = FindUiButtonByAutomationName(view, "开始缓存");

            Assert.Equal("退出选择", toolButton.ToolTip);
            Assert.NotNull(FindVisibleDescendantByText(view, "已选择 2 章"));
            Assert.NotNull(FindVisibleDescendantByText(view, "已有主动缓存批次正在运行，完成或取消后可开始新批次。"));
            Assert.False(startButton.IsEnabled);
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
                    showNoProviderState: true),
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var backButton = FindUiButtonByAutomationName(view, "返回");

            Assert.Null(FindVisibleDescendantByContent(view, "前往 语音服务"));
            Assert.NotNull(FindVisibleDescendantByText(view, "请选择语音服务，或前往语音服务管理完成配置。"));
            Assert.True(backButton.IsEnabled);
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
                Assert.IsType<WpfUiButton>(faultedView.FindName("ErrorProviderMenuButton")),
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
                Assert.IsType<WpfUiButton>(normalView.FindName("ErrorProviderMenuButton")),
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
    public void Player_provider_popup_opens_with_an_available_provider()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = CreateDefaultVisualContext(providers: new ObservableCollection<PlayerProviderItemViewModel>
            {
                new(TestSpeechProviders.Id(1), "回归测试 Provider", isCurrent: true)
            });
            var view = new PlayerView { DataContext = context };
            var window = new Window { Content = view, Width = 1280, Height = 760, ShowInTaskbar = false };
            using var host = WpfWindowHost.Show(window);
            var popup = Assert.IsType<Popup>(view.FindName("ProviderMenuPopup"));

            popup.IsOpen = true;
            popup.Dispatcher.Invoke(DispatcherPriority.Render, static () => { });

            Assert.True(context.HasProviders);
            Assert.True(popup.IsOpen);
            popup.IsOpen = false;
        });
    }

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

    private static PlayerViewLayoutTestContext CreateDefaultVisualContext(
        ObservableCollection<PlayerProviderItemViewModel>? providers = null)
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

        return new PlayerViewLayoutTestContext(chapters, segments, providers: providers);
    }

}
