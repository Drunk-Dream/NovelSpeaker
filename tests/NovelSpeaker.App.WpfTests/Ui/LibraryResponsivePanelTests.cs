using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using NovelSpeaker.App.Features.Library;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class LibraryResponsivePanelTests
{
    [Fact]
    public void Responsive_panel_keeps_cards_within_bounds_and_left_aligns_the_bounded_grid()
    {
        foreach (var scenario in new[]
                 {
                     new LayoutScenario(632d, 2, 308d),
                     new LayoutScenario(1012d, 3, 326.6667d),
                     new LayoutScenario(1172d, 3, 360d),
                     new LayoutScenario(1248d, 4, 300d)
                 })
        {
            WpfTestHost.RunInSta(() =>
            {
                var panel = CreatePanel(8);
                panel.Measure(new Size(scenario.AvailableWidth, 1000));
                panel.Arrange(new Rect(0, 0, scenario.AvailableWidth, panel.DesiredSize.Height));
                panel.UpdateLayout();

                var first = Assert.IsType<Border>(panel.Children[0]);
                var second = Assert.IsType<Border>(panel.Children[1]);
                var nextRow = Assert.IsType<Border>(panel.Children[scenario.Columns]);
                var firstOrigin = first.TranslatePoint(new Point(), panel);
                var secondOrigin = second.TranslatePoint(new Point(), panel);
                var nextRowOrigin = nextRow.TranslatePoint(new Point(), panel);

                Assert.InRange(Math.Abs(first.ActualWidth - scenario.ItemWidth), 0d, 0.5d);
                Assert.InRange(Math.Abs(firstOrigin.X), 0d, 0.5d);
                Assert.InRange(
                    Math.Abs(secondOrigin.X - (scenario.ItemWidth + 16d)),
                    0d,
                    0.5d);
                Assert.InRange(Math.Abs(nextRowOrigin.X), 0d, 0.5d);
                Assert.InRange(Math.Abs(nextRowOrigin.Y - 156d), 0d, 0.5d);
            });
        }
    }

    [Fact]
    public void Responsive_panel_keeps_an_incomplete_last_row_on_the_same_left_grid_baseline()
    {
        WpfTestHost.RunInSta(() =>
        {
            const double availableWidth = 1172d;
            var panel = CreatePanel(5);
            panel.Measure(new Size(availableWidth, 1000));
            panel.Arrange(new Rect(0, 0, availableWidth, panel.DesiredSize.Height));
            panel.UpdateLayout();

            var firstRowLastCard = Assert.IsType<Border>(panel.Children[2]);
            var lastRowFirstCard = Assert.IsType<Border>(panel.Children[3]);
            var lastRowSecondCard = Assert.IsType<Border>(panel.Children[4]);
            var firstRowLastOrigin = firstRowLastCard.TranslatePoint(new Point(), panel);
            var lastRowFirstOrigin = lastRowFirstCard.TranslatePoint(new Point(), panel);
            var lastRowSecondOrigin = lastRowSecondCard.TranslatePoint(new Point(), panel);

            Assert.InRange(Math.Abs(firstRowLastOrigin.X - (2d * (360d + 16d))), 0d, 0.5d);
            Assert.InRange(Math.Abs(lastRowFirstOrigin.X), 0d, 0.5d);
            Assert.InRange(Math.Abs(lastRowSecondOrigin.X - (360d + 16d)), 0d, 0.5d);
            Assert.InRange(
                Math.Abs(availableWidth - firstRowLastOrigin.X - firstRowLastCard.ActualWidth - 60d),
                0d,
                0.5d);
        });
    }

    [Fact]
    public void Responsive_panel_shrinks_a_single_column_below_the_preferred_minimum_without_horizontal_overflow()
    {
        WpfTestHost.RunInSta(() =>
        {
            var panel = CreatePanel(2);
            panel.Measure(new Size(280, 1000));
            panel.Arrange(new Rect(0, 0, 280, panel.DesiredSize.Height));
            panel.UpdateLayout();

            var first = Assert.IsType<Border>(panel.Children[0]);
            Assert.InRange(Math.Abs(first.ActualWidth - 280d), 0d, 0.5d);
            Assert.InRange(Math.Abs(first.TranslatePoint(new Point(), panel).X), 0d, 0.5d);
        });
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

    private static LibraryResponsivePanel CreatePanel(int count)
    {
        var panel = new LibraryResponsivePanel
        {
            MinItemWidth = 300,
            MaxItemWidth = 360,
            HorizontalSpacing = 16,
            VerticalSpacing = 16
        };
        for (var index = 0; index < count; index++)
        {
            panel.Children.Add(new Border { Height = 140 });
        }

        return panel;
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

    private sealed record LayoutScenario(double AvailableWidth, int Columns, double ItemWidth);
}
