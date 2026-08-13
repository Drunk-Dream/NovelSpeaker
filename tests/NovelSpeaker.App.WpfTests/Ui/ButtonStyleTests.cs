using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using NovelSpeaker.StyleGallery;
using Wpf.Ui.Controls;
using Xunit;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
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
        "App.Button.Danger",
        "App.Button.DangerIcon",
        "App.Button.ToolbarValue",
        "App.Button.Floating"
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
            "Buttons.xaml");
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
            var usesUiButton = key is "App.Button.Icon" or "App.Button.DangerIcon";
            Assert.Equal(
                usesUiButton ? "{x:Type ui:Button}" : "Button",
                (string?)resource.Attribute("TargetType"));
            Assert.Equal(
                usesUiButton
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
            "Buttons.xaml");
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
    public void Icon_button_style_has_shared_hit_area_and_disabled_tooltip_contract()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Buttons.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = document.Root?.Elements()
            .Single(resource => (string?)resource.Attribute(xaml + "Key") == "App.Button.Icon");

        Assert.NotNull(style);
        var setters = style!.Elements().Where(element => element.Name.LocalName == "Setter").ToArray();
        Assert.All(
            new[] { "Width", "Height", "MinWidth", "MinHeight" },
            property => Assert.Contains(
                setters,
                setter => (string?)setter.Attribute("Property") == property &&
                          (string?)setter.Attribute("Value") == "{StaticResource App.Size.Icon.Touch}"));
        Assert.Contains(
            setters,
            setter => (string?)setter.Attribute("Property") == "ToolTipService.ShowOnDisabled" &&
                      (string?)setter.Attribute("Value") == "True");
        Assert.DoesNotContain(
            setters,
            setter => (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
                      (string?)setter.Attribute("Value") == "{x:Null}");
    }

    [Fact]
    public void Icon_button_icon_inherits_owner_foreground_in_both_themes()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Icon_button_icon_inherits_owner_foreground_in_both_themes_for_theme(theme);
        }
    }

    private void Icon_button_icon_inherits_owner_foreground_in_both_themes_for_theme(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 };
            var button = new WpfUiButton
            {
                Icon = icon,
                Style = Assert.IsType<Style>(application.FindResource("App.Button.Icon"))
            };
            var window = new Window
            {
                Content = button,
                Width = 96,
                Height = 96,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var expected = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Text.Primary")).Color;
                Assert.Equal(expected, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
                Assert.Equal(expected, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);

                button.IsEnabled = false;
                window.UpdateLayout();
                Assert.Equal(
                    Assert.IsType<SolidColorBrush>(button.Foreground).Color,
                    Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    [Fact]
    public void Icon_button_owner_foreground_updates_in_place_when_theme_changes()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 };
            var button = new WpfUiButton
            {
                Icon = icon,
                Style = Assert.IsType<Style>(application.FindResource("App.Button.Icon"))
            };
            var window = new Window
            {
                Content = button,
                Width = 96,
                Height = 96,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();
                var light = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Text.Primary")).Color;
                Assert.Equal(light, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);

                GalleryThemeRuntime.Apply(GalleryTheme.Dark);
                window.UpdateLayout();
                var dark = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Text.Primary")).Color;

                Assert.NotEqual(light, dark);
                Assert.Equal(dark, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
                Assert.Equal(dark, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
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
                var usesUiButton = key is "App.Button.Icon" or "App.Button.DangerIcon";
                WpfButton button = usesUiButton
                    ? new WpfUiButton
                    {
                        Icon = new SymbolIcon
                        {
                            Symbol = key == "App.Button.DangerIcon"
                                ? SymbolRegular.Delete24
                                : SymbolRegular.Settings24
                        },
                        Style = Assert.IsType<Style>(application.FindResource(key))
                    }
                    : new WpfButton
                    {
                        Content = new WpfTextBlock { Text = $"{key} fixture" },
                        Style = Assert.IsType<Style>(application.FindResource(key))
                    };
                buttons.Add(key, button);
                stack.Children.Add(button);

                Assert.Equal(
                    usesUiButton ? typeof(WpfUiButton) : typeof(WpfButton),
                    button.Style.TargetType);
                Assert.Same(
                    usesUiButton ? providerUiButton : provider,
                    button.Style.BasedOn);

                var expectedTriggers = key == "App.Button.Icon"
                    ? new[] { "IsEnabled" }
                    : new[] { "IsEnabled", "IsMouseOver", "IsPressed" };
                Assert.Equal(
                    expectedTriggers,
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
                    if (button is WpfUiButton iconButton)
                    {
                        Assert.IsType<SymbolIcon>(iconButton.Icon);
                    }
                    else
                    {
                        Assert.Contains(
                            FindDescendants<WpfTextBlock>(button),
                            text => text.Text == $"{pair.Key} fixture");
                    }
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
