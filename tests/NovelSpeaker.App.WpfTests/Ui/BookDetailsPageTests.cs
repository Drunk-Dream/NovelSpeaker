using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class BookDetailsPageTests
{
    private void BookDetailsPage_uses_short_cleanup_action_and_keeps_explicit_entity_delete()
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

            var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
            Assert.Equal("书籍详情", header.Title);
            Assert.Same(page.FindResource("App.Button.Danger"), Assert.IsType<Button>(page.FindName("DeleteBookButton")).Style);
            Assert.Same(page.FindResource("App.Button.Secondary"), Assert.IsType<Button>(page.FindName("ClearCacheButton")).Style);
            Assert.Same(page.FindResource("App.Button.Secondary"), Assert.IsType<Button>(page.FindName("CancelEditButton")).Style);
            Assert.Same(page.FindResource("App.Button.Primary"), Assert.IsType<Button>(page.FindName("SaveBookButton")).Style);
        });
    }

    private void BookDetailsPage_uses_fixed_workspace_layout_and_virtualized_catalog()
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
                WpfWindowHost.Show(window);
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
                Assert.Same(page.FindResource("App.Button.Floating"), locateButton.Style);
                Assert.IsType<AppSectionSurface>(page.FindName("BookInformationSurface"));
                Assert.IsType<AppSectionSurface>(page.FindName("ChapterCatalogSurface"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private void BookDetailsPage_trims_long_chapter_titles_and_exposes_current_chapter_accessibility()
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
            var firstCard = Assert.IsType<Border>(VisualTreeTestHelper.FindDescendant<Border>(
                firstItem,
                border => ReferenceEquals(border.Style, page.FindResource("App.Selection.CurrentItem"))));
            var firstTitle = VisualTreeTestHelper.FindDescendant<TextBlock>(firstItem, static text => text.Text.StartsWith("第一章", StringComparison.Ordinal));
            var firstButton = VisualTreeTestHelper.FindDescendant<Button>(firstItem, static button => AutomationProperties.GetName(button).StartsWith("第 1 章", StringComparison.Ordinal));
            var secondButton = VisualTreeTestHelper.FindDescendant<Button>(secondItem, static button => AutomationProperties.GetName(button).StartsWith("第 2 章", StringComparison.Ordinal));

            Assert.NotNull(firstTitle);
            Assert.NotNull(firstButton);
            Assert.Equal(TextWrapping.NoWrap, firstTitle!.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, firstTitle.TextTrimming);
            Assert.Equal("第一章 这是一个非常非常长的章节标题用于验证详情页目录的单行截断与 Tooltip 展示", firstButton!.ToolTip);
            Assert.InRange(Math.Abs(firstButton.ActualWidth - firstCard.ActualWidth), 0d, 2.1d);
            Assert.InRange(Math.Abs(firstButton.ActualHeight - firstCard.ActualHeight), 0d, 2.1d);
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
            var secondTitle = VisualTreeTestHelper.FindDescendant<TextBlock>(
                secondItem,
                static textBlock => string.Equals(textBlock.Text, "第二章 当前章节标题", StringComparison.Ordinal));
            var secondPercentage = VisualTreeTestHelper.FindDescendant<TextBlock>(
                secondItem,
                static textBlock => string.Equals(textBlock.Text, "25%", StringComparison.Ordinal));
            Assert.NotNull(secondTitle);
            Assert.NotNull(secondPercentage);
            var itemBounds = GetBoundsRelativeToRoot(secondItem, page);
            var titleBounds = GetBoundsRelativeToRoot(secondTitle!, page);
            var percentageBounds = GetBoundsRelativeToRoot(secondPercentage!, page);
            Assert.InRange(titleBounds.Left - itemBounds.Left, 40d, 100d);
            Assert.InRange(itemBounds.Right - percentageBounds.Right, 12d, 20d);
            Assert.True(titleBounds.Right < percentageBounds.Left);
            Assert.Null(VisualTreeTestHelper.FindDescendant<TextBlock>(
                firstItem,
                static textBlock => textBlock.Text.EndsWith('%') &&
                                    textBlock.Visibility == Visibility.Visible));
        });
    }

    private void BookDetailsPage_projects_missing_book_through_status_view()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.HasBook = false;
            viewModel.StatusMessage = "未找到这本书，可能已经被删除。";
            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());
            page.Measure(new Size(900, 640));
            page.Arrange(new Rect(0, 0, 900, 640));
            page.UpdateLayout();

            var workspace = Assert.IsType<Grid>(page.FindName("BookWorkspace"));
            var status = Assert.IsType<AppStatusView>(page.FindName("BookStatusView"));
            Assert.Equal(Visibility.Collapsed, workspace.Visibility);
            Assert.Equal(Visibility.Visible, status.Visibility);
            Assert.Equal(AppStatusKind.Error, status.Status);
            Assert.Equal(viewModel.StatusMessage, status.Description);
        });
    }

    private void BookDetailsPage_keeps_operation_status_visible_when_book_is_loaded()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = CreateViewModel();
            PopulateLayoutState(viewModel, chapterCount: 4);
            viewModel.StatusMessage = "清理缓存失败，请重试。";
            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());
            page.Measure(new Size(900, 640));
            page.Arrange(new Rect(0, 0, 900, 640));
            page.UpdateLayout();

            var inlineStatus = Assert.IsType<Border>(page.FindName("BookInlineStatusMessage"));
            var statusView = Assert.IsType<AppStatusView>(page.FindName("BookStatusView"));
            Assert.Equal(Visibility.Visible, inlineStatus.Visibility);
            Assert.Equal(Visibility.Collapsed, statusView.Visibility);
            Assert.Contains(
                VisualTreeTestHelper.FindDescendants<TextBlock>(inlineStatus),
                text => text.Text == viewModel.StatusMessage);
        });
    }

    private void BookDetailsPage_keeps_all_book_information_reachable_at_reduced_height()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = CreateViewModel();
            PopulateLayoutState(viewModel, chapterCount: 80);
            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());
            using var host = new WpfControlHost(page);
            host.MeasureArrange(new Size(900, 640));

            var informationScroller = Assert.IsType<ScrollViewer>(page.FindName("BookInformationScrollViewer"));
            Assert.True(informationScroller.ScrollableHeight > 0);
            informationScroller.ScrollToEnd();
            page.UpdateLayout();
            Assert.Equal(informationScroller.ScrollableHeight, informationScroller.VerticalOffset, 1);
        });
    }

    private void BookDetailsPage_keeps_workspace_usable_with_long_titles_and_supported_dpi()
    {
        foreach (var (width, scale) in new[] { (900d, 1d), (1280d, 1.25d), (1440d, 1.5d) })
        {
            WpfTestHost.RunInSta(() =>
            {
                var viewModel = CreateViewModel();
                PopulateLayoutState(viewModel, chapterCount: 80);
                viewModel.EditTitle = "一部拥有非常非常长的书名并用于验证详情页编辑布局和截断行为的小说";
                viewModel.CurrentChapterText = "第 41 章 一个同样非常长的当前章节标题用于验证摘要区域不会重叠";
                var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());
                using var host = new WpfControlHost(page);
                var size = new Size(width, 760);
                host.MeasureArrange(size);

                var title = Assert.IsType<TextBox>(page.FindName("TitleTextBox"));
                var author = Assert.IsType<TextBox>(page.FindName("AuthorTextBox"));
                var progress = Assert.IsType<ProgressBar>(page.FindName("ReadingProgressBar"));
                var catalog = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
                Assert.True(title.ActualWidth > 0);
                Assert.True(author.ActualWidth > 0);
                Assert.Same(page.FindResource("App.Input.TextBox.Standard"), title.Style);
                Assert.Same(page.FindResource("App.Input.TextBox.Standard"), author.Style);
                Assert.Same(page.FindResource("App.Progress.Compact"), progress.Style);
                Assert.True(catalog.ActualWidth > 0);
                Assert.True(catalog.ActualHeight > 0);

                var bitmap = host.Render(size, 96 * scale);
                Assert.Equal((int)Math.Round(width * scale), bitmap.PixelWidth);
                Assert.Equal((int)Math.Round(760 * scale), bitmap.PixelHeight);
            });
        }
    }

    private void BookDetails_visual_review_generates_stable_page_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var scenarios = new[]
            {
                new PageVisualReviewScenario("default", 1d, page => PopulateLayoutState(((BookDetailsPage)page).ViewModel, 24)),
                new PageVisualReviewScenario("long-titles", 1.5d, page =>
                {
                    var viewModel = ((BookDetailsPage)page).ViewModel;
                    PopulateLayoutState(viewModel, 80);
                    viewModel.EditTitle = "一部拥有非常非常长的书名并用于验证详情页编辑布局和截断行为的小说";
                    viewModel.CurrentChapterText = "第 41 章 一个同样非常长的当前章节标题用于验证摘要区域不会重叠";
                }),
                new PageVisualReviewScenario("missing-book", 1.5d, page =>
                {
                    var viewModel = ((BookDetailsPage)page).ViewModel;
                    viewModel.HasBook = false;
                    viewModel.StatusMessage = "未找到这本书，可能已经被删除。";
                })
            };

            PageVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "book-details",
                scenarios,
                () => new PageVisualReviewPage(
                    new BookDetailsPage(CreateViewModel(), new FakeNavigationGuardService()),
                    static () => { }));
        });
    }

    private void BookDetailsPage_scrolls_to_current_chapter_when_async_catalog_load_finishes()
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
                WpfWindowHost.Show(window);
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

    [Fact]
    public void Book_details_page_structure_and_projection_contracts_cover_actions_and_status()
    {
        BookDetailsPage_uses_short_cleanup_action_and_keeps_explicit_entity_delete();
        BookDetailsPage_uses_fixed_workspace_layout_and_virtualized_catalog();
        BookDetailsPage_trims_long_chapter_titles_and_exposes_current_chapter_accessibility();
        BookDetailsPage_projects_missing_book_through_status_view();
        BookDetailsPage_keeps_operation_status_visible_when_book_is_loaded();
    }

    [Fact]
    public void Book_details_page_geometry_contracts_cover_reduced_height_long_titles_and_visual_review()
    {
        BookDetailsPage_keeps_all_book_information_reachable_at_reduced_height();
        BookDetailsPage_keeps_workspace_usable_with_long_titles_and_supported_dpi();
        BookDetails_visual_review_generates_stable_page_screenshots();
    }

    [Fact]
    public void Book_details_page_async_catalog_contract_preserves_current_chapter_scroll()
    {
        BookDetailsPage_scrolls_to_current_chapter_when_async_catalog_load_finishes();
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
        viewModel.HasBook = true;
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
