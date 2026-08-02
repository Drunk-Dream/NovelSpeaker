using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Linq;
using NovelSpeaker.App.Shared.Theming;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class IconButtonStyleTests
{
    [Fact]
    public void Tool_icon_button_uses_rounded_state_layer_and_independent_focus_ring()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("BorderlessIconButtonStyle");

            var stateLayer = Assert.IsType<Border>(button.Template.FindName("StateLayer", button));
            var focusRing = Assert.IsType<Border>(button.Template.FindName("KeyboardFocusRing", button));

            Assert.Equal(new CornerRadius(8), stateLayer.CornerRadius);
            Assert.Equal(new CornerRadius(8), focusRing.CornerRadius);
            Assert.Equal(new Thickness(0), button.BorderThickness);
            Assert.Null(button.FocusVisualStyle);
            Assert.Equal(Visibility.Collapsed, focusRing.Visibility);
        });
    }

    [Fact]
    public void Media_icon_button_keeps_a_round_state_layer()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("MediaIconButtonStyle");

            var stateLayer = Assert.IsType<Border>(button.Template.FindName("StateLayer", button));
            var focusRing = Assert.IsType<Border>(button.Template.FindName("KeyboardFocusRing", button));

            Assert.Equal(new CornerRadius(999), stateLayer.CornerRadius);
            Assert.Equal(new CornerRadius(999), focusRing.CornerRadius);
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

    [Fact]
    public void Primary_button_uses_accent_default_focus_and_disabled_states_without_resizing()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("PrimaryButtonStyle");
            var stateLayer = Assert.IsType<Border>(button.Template.FindName("StateLayer", button));
            var focusRing = Assert.IsType<Border>(button.Template.FindName("KeyboardFocusRing", button));
            using var host = ShowButton(button);
            var size = MeasureAndArrange(button);

            Assert.Equal(GetColor("AccentBrush"), GetColor(stateLayer));
            Assert.Equal(new Thickness(1), button.BorderThickness);
            Assert.True(SemanticButtonState.GetIsAccent(button));
            AssertStateTrigger(button, "IsMouseOver");
            AssertStateTrigger(button, "IsPressed");

            button.Focus();
            button.UpdateLayout();
            Assert.Equal(Visibility.Visible, focusRing.Visibility);
            Assert.Equal(size, MeasureAndArrange(button));

            button.IsEnabled = false;
            button.UpdateLayout();
            Assert.Equal(0.5, Assert.IsType<Grid>(button.Template.FindName("RootGrid", button)).Opacity);
            Assert.Equal(size, MeasureAndArrange(button));
        });
    }

    [Fact]
    public void Primary_media_button_uses_accent_default_and_pressed_state_without_resizing()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("PlaybackMediaButtonStyle");
            var stateLayer = Assert.IsType<Border>(button.Template.FindName("StateLayer", button));
            using var host = ShowButton(button);
            var size = MeasureAndArrange(button);

            Assert.Equal(GetColor("AccentBrush"), GetColor(stateLayer));
            Assert.Equal(48, button.Width);
            Assert.Equal(48, button.Height);
            Assert.True(SemanticButtonState.GetIsAccent(button));

            AssertStateTrigger(button, "IsMouseOver");
            AssertStateTrigger(button, "IsPressed");
            Assert.Equal(size, MeasureAndArrange(button));
        });
    }

    [Fact]
    public void Danger_button_uses_danger_default_and_shared_state_triggers()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("DangerButtonStyle");
            var stateLayer = Assert.IsType<Border>(button.Template.FindName("StateLayer", button));
            using var host = ShowButton(button);
            var size = MeasureAndArrange(button);

            Assert.Equal(GetColor("DangerBrush"), GetColor(stateLayer));
            Assert.True(SemanticButtonState.GetIsDanger(button));
            AssertStateTrigger(button, "IsMouseOver");

            button.IsEnabled = false;
            button.UpdateLayout();
            Assert.Equal(0.5, Assert.IsType<Grid>(button.Template.FindName("RootGrid", button)).Opacity);
            Assert.Equal(size, MeasureAndArrange(button));
        });
    }

    [Fact]
    public void Close_button_keeps_tag_and_fixed_geometry_for_focus_and_disabled_states()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = CreateButton("WindowCloseButtonStyle");
            using var host = ShowButton(button);
            var size = MeasureAndArrange(button);
            var initialBorderThickness = button.BorderThickness;

            Assert.Equal("WindowClose", button.Tag);
            button.Focus();
            button.UpdateLayout();
            Assert.Equal(initialBorderThickness, button.BorderThickness);
            Assert.Equal(size, MeasureAndArrange(button));

            button.IsEnabled = false;
            button.UpdateLayout();
            Assert.Equal(initialBorderThickness, button.BorderThickness);
            Assert.Equal(size, MeasureAndArrange(button));
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

    private static Size MeasureAndArrange(Button button)
    {
        button.Measure(new Size(200, 200));
        button.Arrange(new Rect(0, 0, button.DesiredSize.Width, button.DesiredSize.Height));
        button.UpdateLayout();
        return new Size(button.ActualWidth, button.ActualHeight);
    }

    private static Color GetColor(string resourceKey) =>
        Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.FindResource(resourceKey)).Color;

    private static Color GetColor(Border border) =>
        Assert.IsType<SolidColorBrush>(border.Background).Color;

    private static void AssertStateTrigger(Button button, string propertyName)
    {
        var hasTrigger = button.Template.Triggers
            .OfType<MultiDataTrigger>()
            .Any(trigger =>
                trigger.Conditions
                    .OfType<Condition>()
                    .Any(condition => condition.Binding is Binding binding &&
                                     binding.Path.Path?.Contains(propertyName, StringComparison.Ordinal) == true) &&
                trigger.Setters.OfType<Setter>().Any());

        Assert.True(hasTrigger, $"Missing runtime template state trigger for {propertyName}.");
    }

    private static IDisposable ShowButton(Button button)
    {
        var host = new Window
        {
            Content = button,
            Width = 200,
            Height = 200,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow
        };
        host.Show();
        host.UpdateLayout();
        return new WindowLease(host);
    }

    private sealed class WindowLease : IDisposable
    {
        private readonly Window _window;

        public WindowLease(Window window)
        {
            _window = window;
        }

        public void Dispose()
        {
            _window.Close();
        }
    }
}
