using System.IO;
using System.Xml.Linq;
using NovelSpeaker.App.Features.Books.Library;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed class LibraryResponsiveLayoutTests
{
    [Fact]
    public void Responsive_layout_matches_library_width_contracts()
    {
        foreach (var scenario in new[]
                 {
                     new LayoutScenario(632d, 2, 308d),
                     new LayoutScenario(1012d, 3, 326.6667d),
                     new LayoutScenario(1172d, 3, 360d),
                     new LayoutScenario(1248d, 4, 300d)
                 })
        {
            var books = CreateBooks(8);
            var layout = LibraryResponsiveLayout.Create(books, scenario.AvailableWidth);

            Assert.Equal(scenario.Columns, layout.ColumnCount);
            Assert.InRange(Math.Abs(layout.CardWidth - scenario.CardWidth), 0d, 0.001d);
            Assert.Equal((int)Math.Ceiling(8d / scenario.Columns), layout.Rows.Count);
            Assert.All(
                layout.Rows,
                row =>
                {
                    Assert.Equal(scenario.Columns, row.ColumnCount);
                    Assert.True(row.GroupWidth <= scenario.AvailableWidth + 0.001d);
                });
        }
    }

    [Fact]
    public void Responsive_layout_keeps_incomplete_last_row_left_aligned_and_in_order()
    {
        var books = CreateBooks(5);
        var layout = LibraryResponsiveLayout.Create(books, 1172d);

        Assert.Equal(2, layout.Rows.Count);
        Assert.Equal(3, layout.Rows[0].Cards.Count);
        Assert.Equal(2, layout.Rows[1].Cards.Count);
        Assert.Equal(
            books.Select(static book => book.BookId),
            layout.Rows.SelectMany(static row => row.Cards.Select(card => card.Book.BookId)));
        Assert.Equal(0, layout.BookPositions["book-3"].ColumnIndex);
        Assert.Equal(1, layout.BookPositions["book-4"].ColumnIndex);
        Assert.True(layout.Rows[1].Cards[1].IsLast);
    }

    [Fact]
    public void Responsive_layout_uses_one_column_without_horizontal_overflow_when_narrow()
    {
        var layout = LibraryResponsiveLayout.Create(CreateBooks(2), 280d);

        Assert.Equal(1, layout.ColumnCount);
        Assert.Equal(280d, layout.CardWidth);
        Assert.All(layout.Rows, row => Assert.Equal(280d, row.GroupWidth));
    }

    [Fact]
    public void Responsive_layout_builds_10000_books_with_a_bounded_row_projection_shape()
    {
        var books = CreateBooks(10_000);
        var layout = LibraryResponsiveLayout.Create(books, 1248d);

        Assert.Equal(4, layout.ColumnCount);
        Assert.Equal(2500, layout.Rows.Count);
        Assert.Equal(10_000, layout.BookPositions.Count);
        Assert.Same(books[9999], layout.Rows[^1].Cards[^1].Book);
        Assert.Equal(new LibraryBookRowPosition(2499, 3), layout.BookPositions["book-9999"]);
    }

    [Fact]
    public void Book_card_reserves_more_button_space_only_on_the_title_row()
    {
        var root = LocateRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Features",
            "Books",
            "Library",
            "BookCardView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var contentStack = document.Descendants(presentation + "StackPanel")
            .Single(element => (string?)element.Attribute("Grid.Column") == "1");
        Assert.Equal("16,0,0,0", (string?)contentStack.Attribute("Margin"));

        var title = contentStack.Elements(presentation + "TextBlock")
            .First(element => ((string?)element.Attribute("Text"))?.Contains("Item.Title", StringComparison.Ordinal) == true);
        Assert.Equal("0,0,20,0", (string?)title.Attribute("Margin"));

        var progress = Assert.Single(contentStack.Elements(presentation + "ProgressBar"));
        Assert.Equal("0,12,0,0", (string?)progress.Attribute("Margin"));
    }

    private static LibraryBookCardProjection[] CreateBooks(int count)
    {
        var coverGenerator = new BookCoverGenerator();
        return Enumerable.Range(0, count)
            .Select(index => new LibraryBookCardProjection(
                $"book-{index}",
                $"示例小说 {index}",
                "示例作者",
                "第一章",
                "剩余 1 章",
                0.1,
                true,
                "2026-07-01T00:00:00.0000000Z",
                coverGenerator.Generate($"示例小说 {index}"),
                canDelete: true))
            .ToArray();
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

    private sealed record LayoutScenario(double AvailableWidth, int Columns, double CardWidth);
}
