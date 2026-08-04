using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;
using WpfButton = System.Windows.Controls.Button;

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
            Assert.Equal("Button", (string?)resource.Attribute("TargetType"));
            Assert.Equal(
                "{StaticResource Provider.Button}",
                (string?)resource.Attribute("BasedOn"));
            Assert.DoesNotContain(
                resource.Descendants(),
                element => element.Name.LocalName == "ControlTemplate" ||
                           (element.Name.LocalName == "Setter" &&
                            (string?)element.Attribute("Property") == "Template"));
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

            var stack = new StackPanel();
            var buttons = new Dictionary<string, WpfButton>(StringComparer.Ordinal);
            foreach (var key in ButtonStyleKeys)
            {
                var button = new WpfButton
                {
                    Content = new TextBlock { Text = $"{key} fixture" },
                    Style = Assert.IsType<Style>(application.FindResource(key))
                };
                buttons.Add(key, button);
                stack.Children.Add(button);

                Assert.Equal(typeof(WpfButton), button.Style.TargetType);
                Assert.Same(provider, button.Style.BasedOn);
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
                window.Show();
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
