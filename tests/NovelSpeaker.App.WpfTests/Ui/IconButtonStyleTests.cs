using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class IconButtonStyleTests
{
    [Fact]
    public void Tool_icon_button_uses_rounded_state_layer_and_provider_focus_visual()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("BorderlessIconButtonStyle");

            var stateLayer = Assert.IsType<Border>(button.Template.FindName("StateLayer", button));

            Assert.Equal(new CornerRadius(8), stateLayer.CornerRadius);
            Assert.Equal(new Thickness(0), button.BorderThickness);
            Assert.NotNull(button.FocusVisualStyle);
        });
    }

    [Fact]
    public void Media_icon_button_keeps_a_round_state_layer()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("MediaIconButtonStyle");

            var stateLayer = Assert.IsType<Border>(button.Template.FindName("StateLayer", button));

            Assert.Equal(new CornerRadius(999), stateLayer.CornerRadius);
            Assert.Equal(44, button.Width);
            Assert.Equal(44, button.Height);
        });
    }

    [Fact]
    public void Disabled_icon_button_retains_its_layout_and_uses_shared_emphasis()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("BorderlessIconButtonStyle");
            button.IsEnabled = false;
            button.UpdateLayout();

            var rootGrid = Assert.IsType<Grid>(button.Template.FindName("RootGrid", button));

            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.Equal(0.5, rootGrid.Opacity);
            Assert.True(button.Width > 0);
            Assert.True(button.Height > 0);
        });
    }

    private static Button CreateButton(string styleKey)
    {
        var button = new Button
        {
            Style = Assert.IsType<Style>(System.Windows.Application.Current.FindResource(styleKey)),
            Content = "icon"
        };

        button.Measure(new Size(100, 100));
        button.Arrange(new Rect(0, 0, button.DesiredSize.Width, button.DesiredSize.Height));
        button.ApplyTemplate();
        button.UpdateLayout();
        return button;
    }
}
