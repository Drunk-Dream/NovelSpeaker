using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        "App.Button.InteractionHost",
        "App.Button.Floating"
    ];

    private void Button_style_dictionary_contains_only_explicit_provider_based_styles_without_templates()
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
            var usesUiButton = key is "App.Button.Icon" or "App.Button.DangerIcon" or "App.Button.ToolbarValue";
            Assert.Equal(
                usesUiButton ? "{x:Type ui:Button}" : "Button",
                (string?)resource.Attribute("TargetType"));
            Assert.Equal(
                usesUiButton
                    ? "{StaticResource Provider.UiButton}"
                    : "{StaticResource Provider.Button}",
                (string?)resource.Attribute("BasedOn"));
            if (key == "App.Button.InteractionHost")
            {
                Assert.Contains(
                    resource.Descendants(),
                    element => element.Name.LocalName == "ControlTemplate");
            }
            else
            {
                Assert.DoesNotContain(
                    resource.Descendants(),
                    element => element.Name.LocalName == "ControlTemplate" ||
                               (element.Name.LocalName == "Setter" &&
                                (string?)element.Attribute("Property") == "Template"));
            }
        });
    }

    private void Danger_icon_style_is_neutral_until_hover_and_pressed_danger_states()
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

    private void Icon_button_style_has_shared_hit_area_and_disabled_tooltip_contract()
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

        var triggers = style.Elements().Single(element => element.Name.LocalName == "Style.Triggers").Elements();
        Assert.Contains(
            triggers.Single(trigger => (string?)trigger.Attribute("Property") == "IsMouseOver").Elements(),
            setter => (string?)setter.Attribute("Property") == "Foreground" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Foreground.Hover}");
        var pressed = triggers.Single(trigger => (string?)trigger.Attribute("Property") == "IsPressed");
        Assert.Contains(
            pressed.Elements(),
            setter => (string?)setter.Attribute("Property") == "Foreground" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Foreground.Pressed}");
        Assert.Contains(
            pressed.Elements(),
            setter => (string?)setter.Attribute("Property") == "PressedForeground" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Foreground.Pressed}");
    }

    private void Floating_button_style_keeps_the_outer_hit_area_borderless()
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
            .Single(resource => (string?)resource.Attribute(xaml + "Key") == "App.Button.Floating");

        Assert.NotNull(style);
        var setters = style!.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));
        Assert.Equal("Transparent", setters["Background"]);
        Assert.Equal("Transparent", setters["BorderBrush"]);
        Assert.Equal("0", setters["BorderThickness"]);
        Assert.Equal("{x:Null}", setters["Effect"]);
        Assert.All(
            style.Elements().Single(element => element.Name.LocalName == "Style.Triggers").Elements(),
            trigger => Assert.DoesNotContain(
                trigger.Elements(),
                setter => (string?)setter.Attribute("Property") == "Background" &&
                          (string?)setter.Attribute("Value") != "Transparent"));
    }

    private void Interaction_host_style_uses_a_chrome_free_template()
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
            .Single(resource => (string?)resource.Attribute(xaml + "Key") == "App.Button.InteractionHost");

        Assert.NotNull(style);
        Assert.Equal("Button", (string?)style!.Attribute("TargetType"));
        Assert.Equal("{StaticResource Provider.Button}", (string?)style.Attribute("BasedOn"));
        Assert.Contains(
            style.Elements(),
            setter => setter.Name.LocalName == "Setter" &&
                      (string?)setter.Attribute("Property") == "Template");
        Assert.DoesNotContain(
            style.Elements(),
            setter => setter.Name.LocalName == "Setter" &&
                      (string?)setter.Attribute("Property") == "FocusVisualStyle" &&
                      (string?)setter.Attribute("Value") == "{x:Null}");
        Assert.DoesNotContain(
            style.Descendants().Where(element => element.Name.LocalName == "Trigger"),
            trigger => (string?)trigger.Attribute("Property") is "IsMouseOver" or "IsPressed");

        var template = style.Descendants().Single(element => element.Name.LocalName == "ControlTemplate");
        Assert.Equal("{x:Type Button}", (string?)template.Attribute("TargetType"));
        Assert.Contains(
            template.Descendants(),
            element => element.Name.LocalName == "Grid" &&
                       (string?)element.Attribute("Background") == "Transparent");
        Assert.Single(template.Descendants(), element => element.Name.LocalName == "ContentPresenter");
        Assert.DoesNotContain(template.Descendants(), element => element.Name.LocalName == "Border");
    }

    private void Interaction_host_with_empty_content_keeps_a_full_hit_area()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var button = new WpfButton
            {
                Width = 160,
                Height = 48,
                Style = Assert.IsType<Style>(application.FindResource("App.Button.InteractionHost"))
            };
            using var host = new WpfControlHost(button);
            host.MeasureArrange(new Size(160, 48));

            var hit = VisualTreeHelper.HitTest(button, new Point(8, 8));
            Assert.NotNull(hit);
            Assert.IsType<Grid>(hit!.VisualHit);
        });
    }

    private void Interaction_host_keeps_selection_content_hit_testable()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var surface = new Border
            {
                Background = Brushes.Transparent
            };
            var button = new WpfButton
            {
                Width = 160,
                Height = 48,
                Content = surface,
                Style = Assert.IsType<Style>(application.FindResource("App.Button.InteractionHost"))
            };
            using var host = new WpfControlHost(button);
            host.MeasureArrange(new Size(160, 48));

            var hit = VisualTreeHelper.HitTest(button, new Point(80, 24));
            Assert.NotNull(hit);
            Assert.Same(surface, hit!.VisualHit);
        });
    }

    private void Icon_button_icon_inherits_owner_foreground_in_both_themes()
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

    private void Icon_button_owner_foreground_updates_in_place_when_theme_changes()
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

    private void Named_button_styles_keep_provider_templates_and_have_all_interaction_state_triggers()
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
                var usesUiButton = key is "App.Button.Icon" or "App.Button.DangerIcon" or "App.Button.ToolbarValue";
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

                if (key == "App.Button.InteractionHost")
                {
                    Assert.Equal(
                        new[] { "IsEnabled" },
                        button.Style.Triggers
                            .OfType<Trigger>()
                            .Select(trigger => trigger.Property.Name)
                            .Order(StringComparer.Ordinal));
                    continue;
                }

                var expectedTriggers = new[] { "IsEnabled", "IsMouseOver", "IsPressed" };
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


    [Fact]
    public void Button_style_ownership_contracts_cover_dictionary_and_provider_states()
    {
        Button_style_dictionary_contains_only_explicit_provider_based_styles_without_templates();
        Named_button_styles_keep_provider_templates_and_have_all_interaction_state_triggers();
    }

    [Fact]
    public void Button_interaction_contracts_cover_danger_states_hit_area_and_tooltips()
    {
        Danger_icon_style_is_neutral_until_hover_and_pressed_danger_states();
        Icon_button_style_has_shared_hit_area_and_disabled_tooltip_contract();
        Interaction_host_style_uses_a_chrome_free_template();
        Interaction_host_with_empty_content_keeps_a_full_hit_area();
        Interaction_host_keeps_selection_content_hit_testable();
        Floating_button_style_keeps_the_outer_hit_area_borderless();
    }

    [Fact]
    public void Button_foreground_contracts_cover_theme_inheritance_and_updates()
    {
        Icon_button_icon_inherits_owner_foreground_in_both_themes();
        Icon_button_owner_foreground_updates_in_place_when_theme_changes();
        Provider_button_pressed_and_disabled_states_keep_symbol_icons_theme_readable();
    }

    private void Provider_button_pressed_and_disabled_states_keep_symbol_icons_theme_readable()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Dark);
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var buttons = new[]
            {
                CreateIconButton(application, "App.Button.Icon", SymbolRegular.Settings24),
                CreateIconButton(application, "App.Button.ToolbarValue", SymbolRegular.Settings24),
                CreateIconButton(application, "App.Media.Button", SymbolRegular.PlayCircle24)
            };
            var window = new Window
            {
                Content = new StackPanel { Children = { buttons[0], buttons[1], buttons[2] } },
                Width = 360,
                Height = 240,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();
                var pressed = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Interaction.Foreground.Pressed")).Color;
                var disabled = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Interaction.Foreground.Disabled")).Color;

                foreach (var button in buttons)
                {
                    SetPressedValue(button, true);
                    window.UpdateLayout();
                    Assert.True(button.IsPressed);
                    AssertIconForeground(button, pressed);

                    SetPressedValue(button, false);
                    button.IsEnabled = false;
                    window.UpdateLayout();
                    Assert.False(button.IsPressed);
                    AssertIconForeground(button, disabled);
                }
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });

        static WpfUiButton CreateIconButton(
            global::System.Windows.Application application,
            string styleKey,
            SymbolRegular symbol) =>
            new()
            {
                Icon = new SymbolIcon { Symbol = symbol },
                Style = Assert.IsType<Style>(application.FindResource(styleKey))
            };

        static void AssertIconForeground(WpfUiButton button, Color expected)
        {
            var icon = Assert.IsType<SymbolIcon>(button.Icon);
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
            Assert.NotEqual(Colors.Black, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
        }

        static void SetPressedValue(WpfUiButton button, bool value)
        {
            // WPF 10 exposes IsPressed as read-only and has no public input
            // injection API. Keep this implementation detail isolated here so
            // a framework change fails with an explicit visual-contract error.
            var wpfVersion = typeof(ButtonBase).Assembly.GetName().Version;
            Assert.True(
                wpfVersion?.Major >= 10,
                $"Pressed-state adapter requires WPF 10+; actual version: {wpfVersion?.ToString() ?? "unknown"}.");
            var keyField = typeof(ButtonBase).GetField(
                "IsPressedPropertyKey",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(
                keyField);
            Assert.Equal(typeof(DependencyPropertyKey), keyField.FieldType);
            var key = Assert.IsType<DependencyPropertyKey>(keyField.GetValue(null));
            button.SetValue(key, value);
        }
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
