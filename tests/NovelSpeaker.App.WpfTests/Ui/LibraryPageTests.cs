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

            Assert.InRange(items.Items.Count, 1, context.Books.Count);
            Assert.Equal(10_000, context.Books.Count);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(items));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(items));
            Assert.InRange(panel.Children.Count, 1, 100);
        });
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
        private LibrarySortMode selectedSortMode = LibrarySortMode.RecentReading;
    }
}
