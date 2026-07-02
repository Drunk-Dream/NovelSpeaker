using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Views;
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

            var chaptersScrollViewer = FindDescendant<ScrollViewer>(chaptersListBox);
            var segmentsScrollViewer = FindDescendant<ScrollViewer>(segmentListBox);

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

            var returnButton = Assert.IsType<Button>(FindDescendantByContent(view, "回到当前段"));
            Assert.Equal(Visibility.Visible, returnButton.Visibility);
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
            var backButton = Assert.IsType<Button>(FindVisibleDescendantByContent(view, "返回"));

            Assert.Equal(Visibility.Visible, emptyStateButton.Visibility);
            Assert.Equal(Visibility.Visible, noRuleFooter.Visibility);
            Assert.Null(FindVisibleDescendantByContent(view, "播放"));
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
    public void PlayerView_keeps_footer_visible_when_catalog_drawer_is_open_in_compact_layout()
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
                DataContext = new PlayerViewLayoutTestContext(
                    chapters,
                    segments,
                    isCompactLayout: true,
                    isCatalogDrawerOpen: true),
            };

            view.Measure(new Size(900, 640));
            view.Arrange(new Rect(0, 0, 900, 640));
            view.UpdateLayout();

            var drawerList = Assert.IsType<ListBox>(view.FindName("DrawerChaptersListBox"));
            var footer = Assert.IsType<Border>(view.FindName("PlaybackFooterBorder"));

            Assert.True(drawerList.ActualHeight > 0);
            Assert.Equal(Visibility.Visible, footer.Visibility);
            Assert.True(GetBoundsRelativeToRoot(footer, view).Bottom <= view.ActualHeight);
        });
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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

    private static FrameworkElement? FindVisibleDescendantByContent(DependencyObject root, string content)
    {
        var element = FindDescendantByContent(root, content);
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
            bool isCompactLayout = false,
            bool isCatalogDrawerOpen = false)
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
            IsCompactLayout = isCompactLayout;
            IsCatalogDrawerOpen = isCatalogDrawerOpen;
        }

        public IRelayCommand BackCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ToggleCatalogDrawerCommand { get; } = new RelayCommand(() => { });

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

        public string StatusText { get; } = "已暂停";

        public string DetailText { get; } = "已跳转到目标段落，等待播放。";

        public string ErrorText { get; }

        public string DisplayedSegmentCounterText { get; } = "第 33 / 140 段";

        public string SpeedEditorText { get; set; } = "10";

        public string SpeedEditorErrorText { get; } = string.Empty;

        public bool IsCompactLayout { get; }

        public bool IsCatalogDrawerOpen { get; }

        public bool IsRuleMenuOpen { get; set; }

        public bool IsSpeedMenuOpen { get; set; }

        public bool ShouldAutoCenterCurrentSegment { get; } = true;

        public bool ShowReturnToCurrentSegment { get; }

        public bool ShowPlaybackControls { get; }

        public bool ShowNoRuleState { get; }

        public bool ShowPlaybackErrorBar { get; }

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
}
