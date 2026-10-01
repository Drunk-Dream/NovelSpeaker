using System.Windows;
using System.Windows.Media;
using NovelSpeaker.App.Shared.Presentation.Selection;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SelectionSurfaceVisualTests
{
    [Fact]
    public void Current_and_selected_keep_independent_visual_channels()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<System.Windows.Application>(
                System.Windows.Application.Current);
            var selectedBrush = Assert.IsType<SolidColorBrush>(
                application.FindResource("App.Brush.Interaction.Surface.Selected"));
            var accentBrush = Assert.IsType<SolidColorBrush>(
                application.FindResource("App.Brush.Accent.Default"));
            Assert.NotEqual(accentBrush.Color, selectedBrush.Color);

            var current = CreateSurface(application, isCurrent: true, isSelected: false);
            Assert.True(current.IsCurrent);
            Assert.NotSame(selectedBrush, current.Background);

            var combined = CreateSurface(application, isCurrent: true, isSelected: true);
            Assert.True(combined.IsCurrent);
            Assert.Same(selectedBrush, combined.Background);
            Assert.NotSame(accentBrush, combined.BorderBrush);
        });
    }

    private static SelectionSurface CreateSurface(System.Windows.Application application, bool isCurrent, bool isSelected)
    {
        var surface = new SelectionSurface
        {
            Style = Assert.IsType<Style>(application.FindResource("App.Selection.ListItem")),
            DataContext = new { IsCurrent = isCurrent, IsSelected = isSelected }
        };
        surface.Measure(new Size(200, 60));
        surface.Arrange(new Rect(0, 0, 200, 60));
        surface.UpdateLayout();
        return surface;
    }
}
