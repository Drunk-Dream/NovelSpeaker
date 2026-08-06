using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;
using WpfButton = System.Windows.Controls.Button;
using WpfUiButton = Wpf.Ui.Controls.Button;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ButtonStyleTests
{
    private static readonly string[] ButtonStyleKeys =
    [
        "App.Button.Primary",
        "App.Button.Secondary",
        "App.Button.Subtle",
        "App.Button.Icon",
        "App.Button.DangerIcon",
        "App.Button.Danger"
    ];

    [Fact]
    public void Button_style_dictionary_contains_only_explicit_provider_based_styles_without_templates()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "ButtonStyles.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root?.Elements().ToArray() ?? [];

        Assert.Equal(
            ButtonStyleKeys,
            resources.Select(resource => (string?)resource.Attribute(xaml + "Key")).ToArray());
        Assert.All(resources, resource =>
        {
            Assert.Equal("Style", resource.Name.LocalName);
            var key = (string?)resource.Attribute(xaml + "Key");
            Assert.Equal(
                key == "App.Button.DangerIcon" ? "{x:Type ui:Button}" : "Button",
                (string?)resource.Attribute("TargetType"));
            Assert.Equal(
                key == "App.Button.DangerIcon"
                    ? "{StaticResource Provider.UiButton}"
                    : "{StaticResource Provider.Button}",
                (string?)resource.Attribute("BasedOn"));
            Assert.DoesNotContain(
                resource.Descendants(),
                element => element.Name.LocalName == "ControlTemplate" ||
                           (element.Name.LocalName == "Setter" &&
                            (string?)element.Attribute("Property") == "Template"));
        });
    }

    [Fact]
    public void Danger_icon_style_is_neutral_until_hover_and_pressed_danger_states()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "ButtonStyles.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = document.Root?.Elements()
            .Single(resource => (string?)resource.Attribute(xaml + "Key") == "App.Button.DangerIcon");

        Assert.NotNull(style);
        var setters = style!.Elements().Where(element => element.Name.LocalName == "Setter").ToArray();
        Assert.Contains(
            setters,
            setter => (string?)setter.Attribute("Property") == "Background" &&
                      (string?)setter.Attribute("Value") == "Transparent");
        Assert.Contains(
            setters,
            setter => (string?)setter.Attribute("Property") == "Foreground" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Text.Primary}");
        Assert.Contains(
            setters,
            setter => (string?)setter.Attribute("Property") == "MouseOverBackground" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Danger}");
        Assert.Contains(
            setters,
            setter => (string?)setter.Attribute("Property") == "PressedBackground" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Danger.Pressed}");

        var triggers = style.Elements().Single(element => element.Name.LocalName == "Style.Triggers").Elements();
        var hover = triggers.Single(trigger => (string?)trigger.Attribute("Property") == "IsMouseOver");
        Assert.Contains(
            hover.Elements(),
            setter => (string?)setter.Attribute("Property") == "Foreground" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Danger.Text}");
        Assert.DoesNotContain(
            hover.Elements(),
            setter => (string?)setter.Attribute("Property") == "Background");

        var pressed = triggers.Single(trigger => (string?)trigger.Attribute("Property") == "IsPressed");
        Assert.Contains(
            pressed.Elements(),
            setter => (string?)setter.Attribute("Property") == "Foreground" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Danger.Pressed.Text}");
        Assert.DoesNotContain(
            pressed.Elements(),
            setter => (string?)setter.Attribute("Property") == "Background");
    }

    [Fact]
    public void Danger_icon_template_renders_danger_background_when_pointer_is_over_button()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var button = new WpfUiButton
            {
                Width = 48,
                Height = 48,
                Content = new Border
                {
                    Width = 20,
                    Height = 20
                },
                Style = Assert.IsType<Style>(application.FindResource("App.Button.DangerIcon"))
            };
            var window = new Window
            {
                Content = button,
                Width = 96,
                Height = 96,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            var originalCursor = GetCursorPosition();
            try
            {
                WpfWindowHost.Show(window);
                DoEvents();
                window.Left = 120;
                window.Top = 120;
                window.Activate();
                window.UpdateLayout();
                DoEvents();

                var expected = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Danger")).Color;
                var screenPoint = button.PointToScreen(
                    new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                Assert.True(SetCursorPos((int)screenPoint.X, (int)screenPoint.Y));
                Assert.True(button.CaptureMouse());
                DoEvents();
                window.UpdateLayout();

                Assert.True(button.IsMouseOver);
                Assert.Equal(
                    expected,
                    Assert.IsType<SolidColorBrush>(button.MouseOverBackground).Color);
                Assert.Contains(
                    FindDescendants<Border>(button),
                    border => border.Background is SolidColorBrush brush && brush.Color == expected);
            }
            finally
            {
                Mouse.Capture(null);
                SetCursorPos(originalCursor.X, originalCursor.Y);
                window.Close();
            }
        });
    }

    [Fact]
    public void Named_button_styles_keep_provider_templates_and_have_all_interaction_state_triggers()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var provider = Assert.IsType<Style>(application.FindResource("Provider.Button"));
            var providerUiButton = Assert.IsType<Style>(application.FindResource("Provider.UiButton"));

            var stack = new StackPanel();
            var buttons = new Dictionary<string, WpfButton>(StringComparer.Ordinal);
            foreach (var key in ButtonStyleKeys)
            {
                WpfButton button = key == "App.Button.DangerIcon"
                    ? new WpfUiButton
                    {
                        Content = new TextBlock { Text = $"{key} fixture" },
                        Style = Assert.IsType<Style>(application.FindResource(key))
                    }
                    : new WpfButton
                    {
                        Content = new TextBlock { Text = $"{key} fixture" },
                        Style = Assert.IsType<Style>(application.FindResource(key))
                    };
                buttons.Add(key, button);
                stack.Children.Add(button);

                Assert.Equal(
                    key == "App.Button.DangerIcon" ? typeof(WpfUiButton) : typeof(WpfButton),
                    button.Style.TargetType);
                Assert.Same(
                    key == "App.Button.DangerIcon" ? providerUiButton : provider,
                    button.Style.BasedOn);
                Assert.Equal(
                    ["IsEnabled", "IsMouseOver", "IsPressed"],
                    button.Style.Triggers
                        .OfType<Trigger>()
                        .Select(trigger => trigger.Property.Name)
                        .Order(StringComparer.Ordinal));
                Assert.DoesNotContain(
                    button.Style.Triggers.OfType<Trigger>().SelectMany(trigger => trigger.Setters.OfType<Setter>()),
                    setter => setter.Property == FrameworkElement.WidthProperty ||
                              setter.Property == FrameworkElement.HeightProperty ||
                              setter.Property == FrameworkElement.MinWidthProperty ||
                              setter.Property == FrameworkElement.MinHeightProperty ||
                              setter.Property == Control.PaddingProperty ||
                              setter.Property == FrameworkElement.MarginProperty ||
                              setter.Property == Control.HorizontalContentAlignmentProperty ||
                              setter.Property == Control.VerticalContentAlignmentProperty);
            }

            var window = new Window
            {
                Content = stack,
                Width = 640,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                Assert.All(buttons, pair =>
                {
                    var button = pair.Value;
                    Assert.NotNull(button.Template);
                    Assert.True(button.ActualWidth >= 32, pair.Key);
                    Assert.True(button.ActualHeight >= 32, pair.Key);
                    Assert.Contains(
                        FindDescendants<TextBlock>(button),
                        text => text.Text == $"{pair.Key} fixture");
                });

                var enabledSizes = buttons.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.RenderSize,
                    StringComparer.Ordinal);
                foreach (var pair in buttons)
                {
                    pair.Value.IsEnabled = false;
                }

                window.UpdateLayout();

                Assert.All(buttons, pair =>
                {
                    Assert.Equal(enabledSizes[pair.Key].Width, pair.Value.RenderSize.Width);
                    Assert.Equal(enabledSizes[pair.Key].Height, pair.Value.RenderSize.Height);
                    Assert.True(pair.Value.ActualWidth >= 32, pair.Key);
                    Assert.True(pair.Value.ActualHeight >= 32, pair.Key);
                });
            }
            finally
            {
                window.Close();
            }
        });
    }


    private static IReadOnlyList<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var matches = new List<T>();
        Visit(root, matches);
        return matches;

        static void Visit(DependencyObject current, ICollection<T> matches)
        {
            if (current is T match)
            {
                matches.Add(match);
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                Visit(VisualTreeHelper.GetChild(current, index), matches);
            }
        }
    }

    private static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static POINT GetCursorPosition()
    {
        Assert.True(GetCursorPos(out var point));
        return point;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
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
}
