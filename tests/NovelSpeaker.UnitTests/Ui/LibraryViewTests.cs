using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Views;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed partial class LibraryViewTests
{
    [Fact]
    public void LibraryView_uses_internal_scroll_books_area_and_search_clear_icon()
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

            var view = new LibraryView
            {
                DataContext = context
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var booksScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("BooksScrollViewer"));
            var clearSearchButton = FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "清空搜索");

            Assert.NotNull(booksScrollViewer);
            Assert.NotNull(clearSearchButton);
            AssertImportIcon(Assert.IsType<Button>(view.FindName("ToolbarImportButton")));
            Assert.Null(FindDescendant<Button>(view, candidate => Equals(candidate.Content, "清空")));
        });
    }

    [Fact]
    public void LibraryView_uses_import_icon_in_empty_state()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new LibraryView
            {
                DataContext = new LibraryViewLayoutContext()
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            AssertImportIcon(Assert.IsType<Button>(view.FindName("EmptyStateImportButton")));
        });
    }

    [Fact]
    public void LibraryView_shows_no_results_state_with_clear_search_action()
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
            var view = new LibraryView
            {
                DataContext = context
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var clearSearchAction = FindDescendant<Button>(
                view,
                candidate => Equals(candidate.Content, "清空搜索"));

            Assert.NotNull(clearSearchAction);
        });
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

    private static void AssertImportIcon(Button button)
    {
        Assert.Equal("导入小说", AutomationProperties.GetName(button));
        Assert.Equal("导入小说", button.ToolTip);
        Assert.Equal(SymbolRegular.ArrowImport24, Assert.IsType<SymbolIcon>(FindDescendant<SymbolIcon>(button, static _ => true)).Symbol);
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
