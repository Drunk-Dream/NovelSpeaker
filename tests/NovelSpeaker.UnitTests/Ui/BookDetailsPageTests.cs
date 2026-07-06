using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed class BookDetailsPageTests
{
    [Fact]
    public void BookDetailsPage_uses_fixed_workspace_layout_and_virtualized_catalog()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = CreateViewModel();
            PopulateLayoutState(viewModel, chapterCount: 160);

            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());

            page.Measure(new Size(1280, 760));
            page.Arrange(new Rect(0, 0, 1280, 760));
            page.UpdateLayout();

            var chaptersListBox = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
            var chaptersScrollViewer = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(chaptersListBox));
            var virtualizingPanel = VisualTreeTestHelper.FindDescendant<VirtualizingStackPanel>(chaptersListBox);

            Assert.False(page.Content is ScrollViewer);
            Assert.NotNull(virtualizingPanel);
            Assert.True(chaptersScrollViewer.ScrollableHeight > 0);
            Assert.True(GetBoundsRelativeToRoot(chaptersListBox, page).Bottom <= page.ActualHeight);
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
            viewModel.Chapters.Add(new BookDetailsChapterItemViewModel(
                1,
                "第 2 章",
                "第二章 当前章节标题",
                true));

            var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());

            page.Measure(new Size(900, 640));
            page.Arrange(new Rect(0, 0, 900, 640));
            page.UpdateLayout();

            var chaptersListBox = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
            chaptersListBox.UpdateLayout();

            var firstItem = Assert.IsType<ListBoxItem>(chaptersListBox.ItemContainerGenerator.ContainerFromIndex(0));
            var secondItem = Assert.IsType<ListBoxItem>(chaptersListBox.ItemContainerGenerator.ContainerFromIndex(1));
            var firstTitle = FindDescendant<TextBlock>(firstItem, static text => text.Text.StartsWith("第一章", StringComparison.Ordinal));
            var firstButton = FindDescendant<Button>(firstItem, static _ => true);
            var secondButton = FindDescendant<Button>(secondItem, static _ => true);

            Assert.NotNull(firstTitle);
            Assert.NotNull(firstButton);
            Assert.Equal(TextWrapping.NoWrap, firstTitle!.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, firstTitle.TextTrimming);
            Assert.Equal("第一章 这是一个非常非常长的章节标题用于验证详情页目录的单行截断与 Tooltip 展示", firstButton!.ToolTip);
            Assert.NotNull(secondButton);
            Assert.Contains("当前章节", AutomationProperties.GetName(secondButton!));
        });
    }

    private static BookDetailsViewModel CreateViewModel()
    {
        return new BookDetailsViewModel(
            new FakeBookManagementService(),
            new FakeCacheWorkspaceService(),
            new BookCoverGenerator(),
            new FakeFeedbackService(),
            new FakeAppDialogService(),
            new FakeBookDeleteDialogService(),
            new BookCatalogInvalidationState(),
            new FakePlaybackCoordinator(),
            new FakeGuardedNavigationService());
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

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T typed && predicate(typed))
            {
                return typed;
            }

            var descendant = FindDescendant(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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

    private sealed class FakeGuardedNavigationService : IGuardedNavigationService
    {
        public bool IsBypassingGuard => false;

        public Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) => Task.FromResult(true);

        public Task<bool> NavigateAsync(string pageIdOrTargetTag, CancellationToken cancellationToken, bool bypassGuard = false)
            => Task.FromResult(true);

        public Task<bool> NavigateWithHierarchyAsync(Type pageType, object? dataContext, CancellationToken cancellationToken, bool bypassGuard = false)
            => Task.FromResult(true);
    }

    private sealed class FakeBookManagementService : IBookManagementService
    {
        public Task<BookDetails?> GetBookDetailsAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult<BookDetails?>(null);

        public Task<BookDetails> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<long> ClearBookCacheAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult(0L);

        public Task<BookDeleteResult?> DeleteAsync(BookDeleteRequest request, CancellationToken cancellationToken)
            => Task.FromResult<BookDeleteResult?>(null);
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(string bookId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class FakePlaybackCoordinator : IPlaybackCoordinator
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

        public Task SkipCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
