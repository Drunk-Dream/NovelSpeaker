using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class BookDetailsPageTests
{
    [Fact]
    public void BookDetailsPage_uses_short_cleanup_action_and_keeps_explicit_entity_delete()
    {
        WpfTestHost.RunInSta(() =>
        {
            var page = new BookDetailsPage(CreateViewModel(), new FakeNavigationGuardService());

            page.Measure(new Size(900, 640));
            page.Arrange(new Rect(0, 0, 900, 640));
            page.UpdateLayout();

            var actionLabels = VisualTreeTestHelper.FindDescendants<Button>(page)
                .Select(button => button.Content as string)
                .Where(content => content is not null)
                .ToArray();

            Assert.Contains("清理", actionLabels);
            Assert.Contains("删除", actionLabels);
            Assert.Contains("保存", actionLabels);
            Assert.DoesNotContain("清理缓存", actionLabels);
        });
    }

    [Fact]
    public void BookDetailsPage_uses_fixed_workspace_layout_and_virtualized_catalog()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = CreateViewModel();
            PopulateLayoutState(viewModel, chapterCount: 160);

            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());
            var frame = new Frame
            {
                NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
            };
            var window = new Window
            {
                Width = 1280,
                Height = 760,
                Content = frame
            };

            try
            {
                window.Show();
                frame.Navigate(page);
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                var chaptersListBox = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
                var locateButton = Assert.IsType<Button>(page.FindName("LocateCurrentChapterButton"));
                var rootViewport = Assert.IsType<Border>(page.FindName("RootViewport"));
                chaptersListBox.ApplyTemplate();
                chaptersListBox.UpdateLayout();
                var chaptersScrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(chaptersListBox));
                var retiredHelperCopy = VisualTreeTestHelper.FindDescendant<TextBlock>(
                    page,
                    static text => string.Equals(
                        text.Text,
                        "当前版本仅展示已解析章节，点击章节可直接跳转到播放页。",
                        StringComparison.Ordinal));
                var layoutSnapshot =
                    $"inner={chaptersScrollViewer.ScrollableHeight}, frameActual={frame.ActualHeight}, " +
                    $"rootHeight={rootViewport.Height}, rootActual={rootViewport.ActualHeight}, " +
                    $"pageActual={page.ActualHeight}, listActual={chaptersListBox.ActualHeight}";

                Assert.False(page.Content is ScrollViewer);
                Assert.True(ScrollViewer.GetCanContentScroll(chaptersListBox));
                Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(chaptersListBox));
                Assert.True(chaptersScrollViewer.ScrollableHeight > 0, layoutSnapshot);
                Assert.Equal(frame.ActualHeight, rootViewport.Height, 3);
                Assert.True(GetBoundsRelativeToRoot(chaptersListBox, page).Bottom <= rootViewport.ActualHeight, layoutSnapshot);
                Assert.Null(retiredHelperCopy);
                Assert.Equal("定位到当前章节", locateButton.ToolTip);
                Assert.Equal("定位到当前章节", AutomationProperties.GetName(locateButton));
                Assert.Equal(Visibility.Collapsed, locateButton.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void BookDetailsPage_trims_long_chapter_titles_and_exposes_current_chapter_accessibility()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = CreateViewModel();
            PopulateLayoutState(viewModel, chapterCount: 3);
            viewModel.Chapters.Clear();
            viewModel.Chapters.Add(new BookDetailsChapterItemViewModel(
                0,
                "第 1 章",
                "第一章 这是一个非常非常长的章节标题用于验证详情页目录的单行截断与 Tooltip 展示",
                false));
            var currentChapter = new BookDetailsChapterItemViewModel(
                1,
                "第 2 章",
                "第二章 当前章节标题",
                true);
            currentChapter.ApplyCacheStatus(1, 4);
            viewModel.Chapters.Add(currentChapter);

            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());

            page.Measure(new Size(900, 640));
            page.Arrange(new Rect(0, 0, 900, 640));
            page.UpdateLayout();

            var chaptersListBox = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
            chaptersListBox.UpdateLayout();

            var firstItem = Assert.IsType<ListBoxItem>(chaptersListBox.ItemContainerGenerator.ContainerFromIndex(0));
            var secondItem = Assert.IsType<ListBoxItem>(chaptersListBox.ItemContainerGenerator.ContainerFromIndex(1));
            var firstCard = Assert.IsType<Border>(VisualTreeTestHelper.FindDescendant<Border>(firstItem, static border => border.Child is Button));
            var firstTitle = VisualTreeTestHelper.FindDescendant<TextBlock>(firstItem, static text => text.Text.StartsWith("第一章", StringComparison.Ordinal));
            var firstButton = VisualTreeTestHelper.FindDescendant<Button>(firstItem);
            var secondButton = VisualTreeTestHelper.FindDescendant<Button>(secondItem);

            Assert.NotNull(firstTitle);
            Assert.NotNull(firstButton);
            Assert.Equal(TextWrapping.NoWrap, firstTitle!.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, firstTitle.TextTrimming);
            Assert.Equal("第一章 这是一个非常非常长的章节标题用于验证详情页目录的单行截断与 Tooltip 展示", firstButton!.ToolTip);
            Assert.InRange(Math.Abs(firstButton.ActualWidth - firstCard.ActualWidth), 0d, 1d);
            Assert.InRange(Math.Abs(firstButton.ActualHeight - firstCard.ActualHeight), 0d, 1d);
            Assert.NotNull(secondButton);
            Assert.Contains("当前章节", AutomationProperties.GetName(secondButton!));
            Assert.Contains("缓存进度 25%", AutomationProperties.GetName(secondButton), StringComparison.Ordinal);
            Assert.Null(VisualTreeTestHelper.FindDescendant<TextBlock>(
                chaptersListBox,
                static textBlock => string.Equals(textBlock.Text, "当前", StringComparison.Ordinal)));
            Assert.NotNull(VisualTreeTestHelper.FindDescendant<TextBlock>(
                secondItem,
                static textBlock => string.Equals(textBlock.Text, "25%", StringComparison.Ordinal) &&
                                    textBlock.Visibility == Visibility.Visible));
            Assert.Null(VisualTreeTestHelper.FindDescendant<TextBlock>(
                firstItem,
                static textBlock => textBlock.Text.EndsWith('%') &&
                                    textBlock.Visibility == Visibility.Visible));
        });
    }

    [Fact]
    public void BookDetailsPage_keeps_current_and_hover_states_distinguishable()
    {
        WpfTestHost.RunInSta(() =>
        {
            var page = new BookDetailsPage(CreateViewModel(), new FakeNavigationGuardService());
            var style = Assert.IsType<Style>(page.FindResource("CurrentListItemContainerStyle"));
            var hoverTrigger = style.Triggers
                .OfType<MultiDataTrigger>()
                .Single(trigger => trigger.Setters.OfType<Setter>().Any(setter => setter.Property == Border.BackgroundProperty));

            Assert.Equal(2, hoverTrigger.Conditions.Count);
            Assert.All(hoverTrigger.Conditions, condition =>
                Assert.IsType<Binding>(condition.Binding));
            Assert.Contains(
                hoverTrigger.Setters.OfType<Setter>(),
                setter => setter.Property == Border.BackgroundProperty &&
                          setter.Value is DynamicResourceExtension resource &&
                          Equals(resource.ResourceKey, "AccentSubtleHoverBrush"));
        });
    }

    [Fact]
    public void BookDetailsPage_uses_content_only_list_box_item_template()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = CreateViewModel();
            PopulateLayoutState(viewModel, chapterCount: 1);
            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());
            var chaptersListBox = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
            var templateSetter = chaptersListBox.ItemContainerStyle
                .Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == Control.TemplateProperty);
            var template = Assert.IsType<ControlTemplate>(templateSetter.Value);

            Assert.Empty(template.Triggers);

            page.Measure(new Size(900, 640));
            page.Arrange(new Rect(0, 0, 900, 640));
            page.UpdateLayout();

            var item = Assert.IsType<ListBoxItem>(chaptersListBox.ItemContainerGenerator.ContainerFromIndex(0));
            item.ApplyTemplate();
            Assert.IsType<ContentPresenter>(VisualTreeHelper.GetChild(item, 0));
        });
    }

    [Fact]
    public void BookDetailsPage_scrolls_to_current_chapter_when_async_catalog_load_finishes()
    {
        WpfTestHost.RunInSta(() =>
        {
            const int chapterCount = 180;
            const int currentChapterIndex = 90;
            var managementService = new FakeBookManagementService
            {
                Header = new BookDetailsHeader("book-1", "示例小说", "作者甲"),
                Details = CreateDetails(chapterCount, currentChapterIndex)
            };
            var viewModel = CreateViewModel(managementService);
            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService())
            {
                DataContext = new BookDetailsRoute("book-1")
            };
            var frame = new Frame
            {
                NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
            };
            var window = new Window
            {
                Width = 1280,
                Height = 760,
                Content = frame
            };

            try
            {
                window.Show();
                frame.Navigate(page);
                page.OnNavigatedToAsync().GetAwaiter().GetResult();

                var chaptersListBox = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                chaptersListBox.ApplyTemplate();
                chaptersListBox.UpdateLayout();
                var chaptersScrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                    VisualTreeTestHelper.FindDescendant<ScrollViewer>(chaptersListBox));

                WaitUntil(
                    () => chaptersScrollViewer.VerticalOffset > 0 &&
                          viewModel.CurrentChapterItem is { } currentItem &&
                          chaptersListBox.ItemContainerGenerator.ContainerFromItem(currentItem) is FrameworkElement,
                    TimeSpan.FromSeconds(2));

                var currentItem = viewModel.CurrentChapterItem;
                Assert.NotNull(currentItem);
                var currentContainer = chaptersListBox.ItemContainerGenerator.ContainerFromItem(currentItem!) as FrameworkElement;
                Assert.NotNull(currentContainer);
                var currentTop = currentContainer!.TranslatePoint(new Point(0, 0), chaptersScrollViewer).Y;

                Assert.InRange(currentTop, 0d, chaptersScrollViewer.ViewportHeight - currentContainer.ActualHeight);
            }
            finally
            {
                page.OnNavigatedFromAsync().GetAwaiter().GetResult();
                window.Close();
            }
        });
    }

    private static BookDetailsViewModel CreateViewModel(FakeBookManagementService? managementService = null)
    {
        managementService ??= new FakeBookManagementService();
        return new BookDetailsViewModel(
            managementService,
            managementService,
            managementService,
            new FakeCacheWorkspaceService(),
            new FakeAppSettingsService(),
            new BookCoverGenerator(),
            new FakeFeedbackService(),
            new FakeAppDialogService(),
            new FakeBookDeleteDialogService(),
            new BookCatalogInvalidationState(),
            new FakePlaybackCoordinator(),
            new FakeGuardedNavigationService());
    }

    private static BookDetails CreateDetails(int chapterCount, int currentChapterIndex)
    {
        return new BookDetails(
            "book-1",
            "示例小说",
            "作者甲",
            chapterCount,
            currentChapterIndex,
            chapterCount - currentChapterIndex - 1,
            0.5,
            true,
            0,
            Enumerable.Range(0, chapterCount)
                .Select(index => new BookChapterSummary(
                    index,
                    $"第 {index + 1} 章 标题",
                    index * 100,
                    100,
                    index == currentChapterIndex))
                .ToArray());
    }

    private static void PopulateLayoutState(BookDetailsViewModel viewModel, int chapterCount)
    {
        viewModel.Title = "示例小说";
        viewModel.EditTitle = "示例小说";
        viewModel.EditAuthor = "作者甲";
        viewModel.DisplayAuthor = "作者甲";
        viewModel.TotalChapterCountText = $"共 {chapterCount} 章";
        viewModel.CurrentChapterText = "第 41 章 当前章节";
        viewModel.ChapterCatalogSummaryText = $"共 {chapterCount} 章 · 当前第 41 章";
        viewModel.ProgressRatio = 0.4;
        viewModel.ProgressText = "40%";
        viewModel.CacheSizeText = "2 KB";
        viewModel.Cover = new BookCoverGenerator().Generate("示例小说");
        viewModel.StatusMessage = string.Empty;

        viewModel.Chapters.Clear();
        for (var chapterIndex = 0; chapterIndex < chapterCount; chapterIndex++)
        {
            viewModel.Chapters.Add(new BookDetailsChapterItemViewModel(
                chapterIndex,
                $"第 {chapterIndex + 1} 章",
                $"第{chapterIndex + 1}章 标题较长用于验证详情页目录内部滚动与虚拟化工作正常",
                chapterIndex == 40));
        }
    }

    private static Rect GetBoundsRelativeToRoot(FrameworkElement element, FrameworkElement root)
    {
        var topLeft = element.TranslatePoint(new Point(0, 0), root);
        return new Rect(topLeft.X, topLeft.Y, element.ActualWidth, element.ActualHeight);
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

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        Assert.True(predicate());
    }

    private sealed class FakeNavigationGuardService : INavigationGuardService
    {
        public IDisposable Register(Func<CancellationToken, Task<bool>> guard) => new Registration();

        public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        private sealed class Registration : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeGuardedNavigationService : IAppNavigator
    {
        public Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) => Task.FromResult(true);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false)
            => Task.FromResult(true);
    }

    private sealed class FakeBookManagementService : IBookLibraryQuery, IBookMetadataUpdateService, IBookDeletionService
    {
        public BookDetailsHeader? Header { get; init; }

        public BookDetails? Details { get; init; }

        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BookSummary>>([]);

        public Task<BookDetailsHeader?> GetBookDetailsHeaderAsync(string bookId, CancellationToken cancellationToken)
            => Task.FromResult(Header);

        public Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult(Details);

        public Task<BookDetailsHeader> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
            => Task.FromResult<BookDeleteResult?>(null);
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        public event EventHandler<CacheChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChapterCacheStatus>>([]);

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Current => AppSettings.Default;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(Current);
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
            => Task.FromResult(AppConfirmationDecision.Cancel);
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public Task<AppConfirmationDecision> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText,
            CancellationToken cancellationToken)
            => Task.FromResult(AppConfirmationDecision.Cancel);

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(
            string title,
            string message,
            string saveButtonText,
            string discardButtonText,
            string cancelButtonText,
            CancellationToken cancellationToken)
            => Task.FromResult(UnsavedChangesDecision.Cancel);
    }

    private sealed class FakeBookDeleteDialogService : IBookDeleteDialogService
    {
        public Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new BookDeleteDialogResult(false, true));
    }

    private sealed class FakePlaybackCoordinator : IPlaybackBookCommands
    {
        public PlaybackSnapshot CurrentSnapshot { get; } = PlaybackSnapshot.Idle;

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;


        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
