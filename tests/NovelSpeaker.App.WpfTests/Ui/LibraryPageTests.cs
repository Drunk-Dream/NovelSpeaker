using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Features.Books.Library;
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
    public void Library_page_uses_standard_virtualized_rows_and_left_aligned_cards()
    {
        foreach (var bookCount in new[] { 1, 6 })
        {
            WpfTestHost.RunInSta(() =>
            {
                var view = new LibraryPage { DataContext = CreateContext(bookCount) };
                using var host = new WpfControlHost(view);
                host.MeasureArrange(new Size(1280, 760));

                var items = Assert.IsType<ListBox>(view.FindName("BooksItemsControl"));
                var panel = Assert.IsType<VirtualizingStackPanel>(
                    VisualTreeTestHelper.FindDescendant<VirtualizingStackPanel>(items));

                Assert.True(VirtualizingPanel.GetIsVirtualizing(items));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(items));

                var firstCard = Assert.IsType<BookCardView>(
                    VisualTreeTestHelper.FindDescendant<BookCardView>(items));
                var firstOrigin = firstCard.TranslatePoint(new Point(), items);
                Assert.InRange(Math.Abs(firstOrigin.X), 0d, 1d);

                if (bookCount == 6)
                {
                    Assert.Equal(2, items.Items.Count);
                }
            });
        }
    }

    [Fact]
    public void Library_page_realizes_only_the_visible_window_for_a_large_catalog()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new LibraryViewLayoutContext
            {
                HasBooks = true,
                HasVisibleBooks = true,
                LibrarySummaryText = "共 10000 本 · 最近阅读优先"
            };
            var cover = new BookCoverGenerator().Generate("示例小说");
            for (var index = 0; index < 10_000; index++)
            {
                context.Books.Add(new LibraryBookCardProjection(
                    $"book-{index}",
                    $"示例小说 {index}",
                    "示例作者",
                    "第一章",
                    "剩余 1 章",
                    0.1,
                    true,
                    "2026-07-01T00:00:00.0000000Z",
                    cover,
                    canDelete: true));
            }
            context.SetRows(1232d);

            var view = new LibraryPage { DataContext = context };
            using var host = new WpfControlHost(view);
            host.MeasureArrange(new Size(1280, 760));

            var items = Assert.IsType<ListBox>(view.FindName("BooksItemsControl"));
            var panel = Assert.IsType<VirtualizingStackPanel>(
                VisualTreeTestHelper.FindDescendant<VirtualizingStackPanel>(items));

            Assert.Equal(3334, items.Items.Count);
            Assert.Equal(10_000, context.Books.Count);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(items));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(items));
            Assert.InRange(panel.Children.Count, 1, 100);
        });
    }

    [Fact]
    public async Task Library_page_realizes_cards_when_rows_are_published_after_initial_layout()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var context = new LibraryViewLayoutContext
            {
                HasBooks = true,
                HasVisibleBooks = true,
                LibrarySummaryText = "共 1 本 · 最近阅读优先"
            };
            var view = new LibraryPage { DataContext = context };
            using var host = new WpfControlHost(view);
            host.MeasureArrange(new Size(1280, 760));

            var items = Assert.IsType<ListBox>(view.FindName("BooksItemsControl"));
            Assert.Empty(items.Items);

            await view.Dispatcher.InvokeAsync(
                new Action(() =>
                {
                    context.Books.Add(new LibraryBookCardProjection(
                        "book-async",
                        "异步提交的小说",
                        "示例作者",
                        "第一章",
                        "剩余 1 章",
                        0.1,
                        true,
                        "2026-07-01T00:00:00.0000000Z",
                        new BookCoverGenerator().Generate("异步提交的小说"),
                        canDelete: true));
                    context.SetRows(1232d);
                }),
                DispatcherPriority.Background);
            view.UpdateLayout();

            Assert.Single(context.Books);
            Assert.Single(items.Items);
            Assert.NotNull(VisualTreeTestHelper.FindDescendant<BookCardView>(items));
            Assert.NotNull(
                VisualTreeTestHelper.FindDescendant<TextBlock>(
                    view,
                    candidate => candidate.Text == "共 1 本 · 最近阅读优先"));
        });
    }

    private static LibraryViewLayoutContext CreateContext(
        int bookCount,
        bool longTitles = false,
        double availableWidth = 1232d)
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
            context.Books.Add(new LibraryBookCardProjection(
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

        context.SetRows(availableWidth);

        return context;
    }

    private sealed partial class LibraryViewLayoutContext : ObservableObject
    {
        public ObservableCollection<LibraryBookCardProjection> Books { get; } = [];
        public ObservableCollection<LibraryBookRowProjection> Rows { get; private set; } = [];
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

        public void SetRows(double availableWidth)
        {
            Rows = new ObservableCollection<LibraryBookRowProjection>(
                LibraryResponsiveLayout.Create(Books.ToArray(), availableWidth).Rows);
            OnPropertyChanged(nameof(Rows));
        }

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
