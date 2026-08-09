using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed partial class LibraryPageTests
{
    [Fact]
    public void LibraryPage_uses_internal_scroll_books_area_and_search_clear_icon()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new LibraryViewLayoutContext
            {
                HasBooks = true,
                HasVisibleBooks = true,
                HasSearchText = true,
                SearchText = "三体",
                LibrarySummaryText = "共 1 本 · 最近阅读优先"
            };
            context.Books.Add(new LibraryBookItemViewModel(
                "book-1",
                "三体",
                "刘慈欣",
                "第一章 科学边界",
                "剩余 5 章",
                0.5,
                true,
                "2026-06-30T00:00:00.0000000Z",
                new BookCoverGenerator().Generate("三体"),
                canDelete: true));

            var view = new LibraryPage
            {
                DataContext = context
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var booksScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("BooksScrollViewer"));
            var clearSearchButton = VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "清空搜索");

            Assert.NotNull(booksScrollViewer);
            Assert.NotNull(clearSearchButton);
            AssertImportIcon(Assert.IsType<Button>(view.FindName("ToolbarImportButton")));
            Assert.Same(view.FindResource("App.Button.Icon"), clearSearchButton.Style);
            Assert.Same(
                view.FindResource("App.Input.TextBox.Standard"),
                Assert.IsType<TextBox>(view.FindName("SearchTextBox")).Style);
            Assert.Same(
                view.FindResource("App.Input.ComboBox.Standard"),
                Assert.IsType<ComboBox>(view.FindName("SortComboBox")).Style);
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(view, candidate => Equals(candidate.Content, "清空")));
        });
    }

    [Fact]
    public void LibraryPage_uses_import_icon_in_empty_state()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new LibraryPage
            {
                DataContext = new LibraryViewLayoutContext()
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var header = Assert.IsType<AppPageHeader>(view.FindName("PageHeader"));
            Assert.Equal("书库", header.Title);
            Assert.Null(header.BackCommand);

            var emptyStatus = Assert.IsType<AppStatusView>(view.FindName("EmptyLibraryStatusView"));
            Assert.Equal(AppStatusKind.Empty, emptyStatus.Status);
            Assert.Equal("尚未导入小说", emptyStatus.Title);
            Assert.Equal(Visibility.Visible, emptyStatus.Visibility);

            var importButton = Assert.IsType<Button>(view.FindName("EmptyStateImportButton"));
            Assert.Equal("导入小说", importButton.Content);
            Assert.Equal("导入小说", AutomationProperties.GetName(importButton));
            Assert.Same(view.FindResource("App.Button.Primary"), importButton.Style);
        });
    }

    [Fact]
    public void LibraryPage_shows_no_results_state_with_clear_search_action()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new LibraryViewLayoutContext
            {
                HasBooks = true,
                HasVisibleBooks = false,
                HasSearchText = true,
                SearchText = "missing",
                LibrarySummaryText = "共 1 本 · 最近阅读优先"
            };
            var view = new LibraryPage
            {
                DataContext = context
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var clearSearchAction = VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => Equals(candidate.Content, "清空搜索"));

            Assert.NotNull(clearSearchAction);
            Assert.Same(view.FindResource("App.Button.Secondary"), clearSearchAction.Style);

            var noResults = Assert.IsType<AppStatusView>(view.FindName("NoResultsStatusView"));
            Assert.Equal(AppStatusKind.NoResult, noResults.Status);
            Assert.Equal(Visibility.Visible, noResults.Visibility);
        });
    }

    [Fact]
    public void LibraryPage_is_transparent_and_uses_no_legacy_resources()
    {
        var xamlPath = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "Library",
            "LibraryPage.xaml");
        var source = File.ReadAllText(xamlPath);
        var pageElement = XDocument.Load(xamlPath).Root!;

        Assert.Equal("Transparent", pageElement.Attribute("Background")?.Value);
        Assert.Contains("AppPageHeader", source, StringComparison.Ordinal);
        Assert.Contains("AppStatusView", source, StringComparison.Ordinal);
        Assert.Contains("App.Feedback.InlineMessage", source, StringComparison.Ordinal);

        foreach (var legacyKey in new[]
                 {
                     "PagePadding",
                     "PageTitleTextBlockStyle",
                     "PrimaryTextBlockStyle",
                     "SecondaryTextBlockStyle",
                     "StrongTextBlockStyle",
                     "BorderlessIconButtonStyle"
                 })
        {
            Assert.DoesNotContain(legacyKey, source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(520, 1d)]
    [InlineData(960, 1.25d)]
    [InlineData(1280, 1.5d)]
    public void LibraryPage_adapts_toolbar_and_book_grid_across_widths_and_dpi(double width, double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = CreateContext(6, longTitles: true);
            var view = new LibraryPage { DataContext = context };
            using var host = new WpfControlHost(view);
            var size = new Size(width, 760);
            host.MeasureArrange(size);

            var toolbar = Assert.IsType<WrapPanel>(view.FindName("LibraryToolbar"));
            var search = Assert.IsType<TextBox>(view.FindName("SearchTextBox"));
            var sort = Assert.IsType<ComboBox>(view.FindName("SortComboBox"));
            var items = Assert.IsType<ItemsControl>(view.FindName("BooksItemsControl"));
            Assert.True(toolbar.ActualWidth > 0);
            Assert.True(toolbar.ActualWidth <= width - 48 + 0.5);
            Assert.True(search.ActualWidth >= 260);
            Assert.True(sort.ActualWidth >= 160);
            Assert.Equal(6, items.Items.Count);

            var bitmap = host.Render(size, 96 * scale);
            Assert.Equal((int)Math.Round(width * scale), bitmap.PixelWidth);
            Assert.Equal((int)Math.Round(760 * scale), bitmap.PixelHeight);
        });
    }

    [Fact]
    public void Library_visual_review_generates_stable_page_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var scenarios = new[]
            {
                new PageVisualReviewScenario("empty", 1d, page => page.DataContext = CreateContext(0)),
                new PageVisualReviewScenario("no-results", 1.5d, page =>
                {
                    var context = CreateContext(1);
                    context.HasVisibleBooks = false;
                    context.HasSearchText = true;
                    context.SearchText = "missing";
                    page.DataContext = context;
                }),
                new PageVisualReviewScenario("books", 1d, page => page.DataContext = CreateContext(4)),
                new PageVisualReviewScenario("long-titles", 1.5d, page => page.DataContext = CreateContext(4, longTitles: true))
            };

            PageVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "library",
                scenarios,
                () => new PageVisualReviewPage(new LibraryPage(), static () => { }));
        });
    }

    private static void AssertImportIcon(Button button)
    {
        Assert.Equal("导入小说", AutomationProperties.GetName(button));
        Assert.Equal("导入小说", button.ToolTip);
        Assert.Same(button.FindResource("App.Button.Icon"), button.Style);
        Assert.Equal(SymbolRegular.ArrowImport24, Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(button)).Symbol);
    }

    private static LibraryViewLayoutContext CreateContext(int bookCount, bool longTitles = false)
    {
        var context = new LibraryViewLayoutContext
        {
            HasBooks = bookCount > 0,
            HasVisibleBooks = bookCount > 0,
            LibrarySummaryText = $"共 {bookCount} 本 · 最近阅读优先"
        };
        for (var index = 0; index < bookCount; index++)
        {
            var title = longTitles
                ? $"一部拥有非常非常长标题并用于验证省略显示与提示信息的小说 {index + 1}"
                : $"示例小说 {index + 1}";
            context.Books.Add(new LibraryBookItemViewModel(
                $"book-{index + 1}",
                title,
                $"示例作者 {index + 1}",
                $"第 {index + 1} 章 当前阅读章节标题",
                $"剩余 {bookCount - index} 章",
                (index + 1d) / Math.Max(bookCount, 1),
                true,
                $"2026-07-{index + 1:00}T00:00:00.0000000Z",
                new BookCoverGenerator().Generate(title),
                canDelete: true));
        }

        return context;
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

    private sealed partial class LibraryViewLayoutContext : ObservableObject
    {
        public ObservableCollection<LibraryBookItemViewModel> Books { get; } = [];
        public ObservableCollection<LibrarySortOption> AvailableSortOptions { get; } =
        [
            new LibrarySortOption(LibrarySortMode.RecentReading, "最近阅读"),
            new LibrarySortOption(LibrarySortMode.Title, "书名")
        ];

        public RelayCommand ClearSearchCommand { get; } = new(() => { });
        public RelayCommand OpenBookCommand { get; } = new(() => { });
        public RelayCommand OpenBookDetailsCommand { get; } = new(() => { });
        public RelayCommand DeleteBookCommand { get; } = new(() => { });
        public LibraryScrollState ScrollState { get; } = new();

        [ObservableProperty]
        private bool hasBooks;

        [ObservableProperty]
        private bool hasVisibleBooks;

        [ObservableProperty]
        private bool hasSearchText;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string librarySummaryText = string.Empty;

        [ObservableProperty]
        private string importStatusMessage = string.Empty;

        [ObservableProperty]
        private string statusMessage = string.Empty;

        [ObservableProperty]
        private LibrarySortMode selectedSortMode = LibrarySortMode.RecentReading;
    }
}
