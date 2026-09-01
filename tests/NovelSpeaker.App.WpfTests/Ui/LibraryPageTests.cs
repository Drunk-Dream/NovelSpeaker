using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    public void Library_page_surface_contracts_cover_scroll_empty_states_and_responsive_layout()
    {
        LibraryPage_uses_internal_scroll_books_area_and_search_clear_icon();
        LibraryPage_uses_import_icon_in_empty_state();
        LibraryPage_shows_no_results_state_with_clear_search_action();
        LibraryPage_adapts_toolbar_and_book_grid_across_widths_and_dpi();
    }

    [Fact]
    public void Library_page_visual_contracts_keep_screenshot_review_repeatable()
    {
        Library_visual_review_generates_stable_page_screenshots();
    }

    private void LibraryPage_uses_internal_scroll_books_area_and_search_clear_icon()
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
            var header = Assert.IsType<AppPageHeader>(view.FindName("PageHeader"));
            var toolbar = Assert.IsType<WrapPanel>(view.FindName("LibraryToolbar"));
            var clearSearchButton = VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "清空搜索");

            Assert.NotNull(booksScrollViewer);
            Assert.Same(toolbar, header.Actions);
            Assert.InRange(
                Math.Abs(GetCenterY(header, view) - GetCenterY(toolbar, view)),
                0d,
                1d);
            Assert.NotNull(clearSearchButton);
            AssertImportIcon(Assert.IsType<Wpf.Ui.Controls.Button>(view.FindName("ToolbarImportButton")));
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

    private void LibraryPage_uses_import_icon_in_empty_state()
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

    private void LibraryPage_shows_no_results_state_with_clear_search_action()
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

    private void LibraryPage_adapts_toolbar_and_book_grid_across_widths_and_dpi()
    {
        foreach (var (width, scale) in new[] { (900d, 1d), (960d, 1.25d), (1280d, 1.5d) })
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
                var booksScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("BooksScrollViewer"));
                var items = Assert.IsType<ItemsControl>(view.FindName("BooksItemsControl"));
                var panel = Assert.IsType<LibraryResponsivePanel>(
                    VisualTreeTestHelper.FindDescendant<LibraryResponsivePanel>(items));
                Assert.True(toolbar.ActualWidth > 0);
                Assert.True(toolbar.ActualWidth <= width - 48 + 0.5);
                Assert.True(search.ActualWidth >= 260);
                Assert.True(sort.ActualWidth >= 160);
                Assert.Equal(6, items.Items.Count);
                Assert.InRange(Math.Abs(panel.ActualWidth - booksScrollViewer.ViewportWidth), 0d, 1d);
                Assert.True(
                    booksScrollViewer.ExtentWidth <= booksScrollViewer.ViewportWidth + 1d,
                    $"Books extent {booksScrollViewer.ExtentWidth:0.##} exceeded viewport {booksScrollViewer.ViewportWidth:0.##}.");
                AssertLibraryGridGeometry(panel, width == 1280d ? 3 : null);

                var bitmap = host.Render(size, 96 * scale);
                Assert.Equal((int)Math.Round(width * scale), bitmap.PixelWidth);
                Assert.Equal((int)Math.Round(760 * scale), bitmap.PixelHeight);
            });
        }
    }

    private static void AssertLibraryGridGeometry(LibraryResponsivePanel panel, int? expectedColumns)
    {
        Assert.True(panel.ActualWidth > 0);
        Assert.NotEmpty(panel.Children);

        var firstRowY = panel.Children[0].TranslatePoint(new Point(), panel).Y;
        var firstRow = panel.Children
            .OfType<FrameworkElement>()
            .Where(child => Math.Abs(child.TranslatePoint(new Point(), panel).Y - firstRowY) < 1d)
            .ToArray();
        var columns = firstRow.Length;
        var expectedByViewport = Math.Max(
            1,
            (int)Math.Floor((panel.ActualWidth + 16d) / (300d + 16d)));

        Assert.Equal(expectedByViewport, columns);
        if (expectedColumns is not null)
        {
            Assert.Equal(expectedColumns.Value, columns);
        }

        var rawItemWidth = (panel.ActualWidth - ((columns - 1) * 16d)) / columns;
        var expectedItemWidth = Math.Min(360d, rawItemWidth);
        Assert.All(
            firstRow,
            child => Assert.InRange(Math.Abs(child.ActualWidth - expectedItemWidth), 0d, 1d));

        for (var index = 1; index < firstRow.Length; index++)
        {
            var previousOrigin = firstRow[index - 1].TranslatePoint(new Point(), panel);
            var currentOrigin = firstRow[index].TranslatePoint(new Point(), panel);
            Assert.InRange(
                Math.Abs(currentOrigin.X - previousOrigin.X - firstRow[index - 1].ActualWidth - 16d),
                0d,
                1d);
        }

        Assert.All(
            panel.Children.OfType<FrameworkElement>(),
            child =>
            {
                var origin = child.TranslatePoint(new Point(), panel);
                Assert.True(origin.X >= -1d);
                Assert.True(origin.X + child.ActualWidth <= panel.ActualWidth + 1d);
            });
    }

    private void Library_visual_review_generates_stable_page_screenshots()
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

    private static double GetCenterY(FrameworkElement element, FrameworkElement ancestor)
    {
        var origin = element.TransformToAncestor(ancestor).Transform(new Point());
        return origin.Y + (element.ActualHeight / 2d);
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
