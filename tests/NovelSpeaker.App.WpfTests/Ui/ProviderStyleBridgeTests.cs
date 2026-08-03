using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using NovelSpeaker.App.Shared.Theming;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using Xunit;
using WpfButton = System.Windows.Controls.Button;
using WpfPasswordBox = System.Windows.Controls.PasswordBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ProviderStyleBridgeTests
{
    private static readonly string[] BridgeKeys =
    [
        "Provider.Button",
        "Provider.TextBox",
        "Provider.PasswordBox",
        "Provider.ComboBox",
        "Provider.CheckBox",
        "Provider.ToggleSwitch",
        "Provider.NavigationViewItem",
        "Provider.Slider"
    ];

    [Fact]
    public void App_loads_provider_dictionaries_then_bridge_then_application_dictionaries()
    {
        WpfTestHost.RunInSta(() =>
        {
            var dictionaries = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current).Resources.MergedDictionaries;

            Assert.Equal(5, dictionaries.Count);
            Assert.True(IsWpfUiThemeDictionary(dictionaries[0]));
            Assert.Contains("Wpf.Ui", dictionaries[0].Source?.OriginalString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "/Resources/Theme/",
                dictionaries[0].Source?.OriginalString?.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
            Assert.IsType<ControlsDictionary>(dictionaries[1]);
            Assert.EndsWith(
                "Shared/Theming/Provider/ProviderStyleBridge.xaml",
                dictionaries[2].Source?.OriginalString,
                StringComparison.Ordinal);
            Assert.Equal(
                BridgeKeys.Order(StringComparer.Ordinal),
                dictionaries[2].Keys.Cast<object>().OfType<string>().Order(StringComparer.Ordinal));
            Assert.All(BridgeKeys, key => Assert.IsType<Style>(dictionaries[2][key]));
            Assert.EndsWith(
                "Shared/Theming/Resources/DesignTokens.xaml",
                dictionaries[3].Source?.OriginalString,
                StringComparison.Ordinal);
            Assert.EndsWith(
                "Shared/Theming/Resources/SemanticStyles.xaml",
                dictionaries[4].Source?.OriginalString,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Bridge_contains_only_explicit_alias_styles_and_no_templates()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Provider",
            "ProviderStyleBridge.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root?.Elements().ToArray() ?? [];

        Assert.Equal(BridgeKeys, resources
            .Select(resource => (string?)resource.Attribute(xaml + "Key"))
            .ToArray());
        Assert.All(resources, resource => Assert.Equal("Style", resource.Name.LocalName));
        Assert.DoesNotContain(resources, resource => resource.Descendants().Any(
            element => element.Name.LocalName == "ControlTemplate" ||
                       (element.Name.LocalName == "Setter" &&
                        (string?)element.Attribute("Property") == "Template")));
    }

    [Fact]
    public void Bridge_styles_resolve_to_non_empty_provider_templates_in_both_themes()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var runtime = new WpfUiThemeRuntime();
            var before = CaptureProviderChain(application);

            var styles = BridgeKeys.ToDictionary(
                key => key,
                key => Assert.IsType<Style>(application.FindResource(key)),
                StringComparer.Ordinal);
            Assert.All(styles.Values, style => Assert.NotNull(style.BasedOn));

            foreach (var key in BridgeKeys)
            {
                var control = CreateControl(key, styles[key]);
                PrepareControl(control);
                Assert.True(
                    control.Template is not null,
                    $"Provider alias did not resolve a template: {key}");
            }

            runtime.ApplyDarkTheme();
            var dark = CaptureProviderChain(application);
            runtime.ApplyLightTheme();
            var light = CaptureProviderChain(application);

            Assert.Equal(before.DictionaryCount, dark.DictionaryCount);
            Assert.Equal(before.DictionaryCount, light.DictionaryCount);
            Assert.Equal(before.ProviderDictionarySignatures, dark.ProviderDictionarySignatures);
            Assert.Equal(before.ProviderDictionarySignatures, light.ProviderDictionarySignatures);
            Assert.All(
                styles,
                pair => Assert.Same(pair.Value, application.FindResource(pair.Key)));
        });
    }

    [Fact]
    public void Provider_button_preserves_content_alignment_and_triggers_focus_and_disabled_states()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = new WpfButton
            {
                Style = Assert.IsType<Style>(
                    global::System.Windows.Application.Current.FindResource("Provider.Button")),
                Content = "alignment fixture",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var window = new TestWindow(button);
            try
            {
                window.Show();
                button.Measure(new Size(240, 60));
                button.Arrange(new Rect(0, 0, 240, 60));
                button.ApplyTemplate();
                button.UpdateLayout();

                Assert.NotNull(button.Template);
                Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
                Assert.True(button.Focus());
                Assert.True(button.IsKeyboardFocused);

                button.IsEnabled = false;
                button.UpdateLayout();
                Assert.False(button.IsEnabled);
                Assert.NotNull(button.Template);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Control CreateControl(string key, Style style) => key switch
    {
        "Provider.Button" => new WpfButton { Content = "button", Style = style },
        "Provider.TextBox" => new WpfTextBox { Text = "text", Style = style },
        "Provider.PasswordBox" => new WpfPasswordBox { Password = "fixture", Style = style },
        "Provider.ComboBox" => new ComboBox
        {
            ItemsSource = new[] { "Light", "Dark" },
            SelectedIndex = 0,
            Style = style
        },
        "Provider.CheckBox" => new CheckBox { Content = "check", Style = style },
        "Provider.ToggleSwitch" => new ToggleSwitch { Content = "toggle", Style = style },
        "Provider.NavigationViewItem" => new NavigationViewItem { Content = "navigation", Style = style },
        "Provider.Slider" => new Slider { Minimum = 0, Maximum = 1, Value = 0.5, Style = style },
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown bridge key.")
    };

    private static void PrepareControl(Control control)
    {
        if (control is NavigationViewItem navigationItem)
        {
            var navigation = new NavigationView();
            navigation.MenuItems.Add(navigationItem);
            var window = new TestWindow(navigation);
            try
            {
                window.Show();
                window.UpdateLayout();
                navigationItem.Measure(new Size(240, 60));
                navigationItem.Arrange(new Rect(0, 0, 240, 60));
                navigationItem.ApplyTemplate();
                navigationItem.UpdateLayout();
            }
            finally
            {
                window.Close();
            }

            return;
        }

        control.Measure(new Size(240, 60));
        control.Arrange(new Rect(0, 0, 240, 60));
        control.ApplyTemplate();
        control.UpdateLayout();
    }

    private static ProviderChainSnapshot CaptureProviderChain(global::System.Windows.Application application)
    {
        var dictionaries = application.Resources.MergedDictionaries;
        Assert.True(IsWpfUiThemeDictionary(dictionaries[0]));
        Assert.IsType<ControlsDictionary>(dictionaries[1]);
        Assert.Equal(typeof(ResourceDictionary), dictionaries[2].GetType());
        Assert.EndsWith(
            "ProviderStyleBridge.xaml",
            dictionaries[2].Source?.OriginalString,
            StringComparison.Ordinal);
        Assert.Equal(
            BridgeKeys.Order(StringComparer.Ordinal),
            dictionaries[2].Keys.Cast<object>().OfType<string>().Order(StringComparer.Ordinal));

        return new ProviderChainSnapshot(
            dictionaries.Count,
            dictionaries.Take(3)
                .Select((dictionary, index) => DescribeProviderDictionary(dictionary, index))
                .ToArray());
    }

    private static string DescribeProviderDictionary(ResourceDictionary dictionary, int index) =>
        $"role:{index switch
        {
            0 => "Wpf.Ui.ThemeProvider",
            1 => "Wpf.Ui.ControlsProvider",
            2 => "NovelSpeaker.ProviderStyleBridge",
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        }}|" +
        $"source:{NormalizeThemeSource(dictionary, index)}|" +
        $"type:{(index == 0 ? "provider-theme-wrapper" : dictionary.GetType().AssemblyQualifiedName)}|" +
        $"keys:{string.Join(",", dictionary.Keys.Cast<object>().Select(DescribeKey).Order(StringComparer.Ordinal))}";

    private static string NormalizeThemeSource(ResourceDictionary dictionary, int index)
    {
        if (index != 0)
        {
            return dictionary.Source?.OriginalString?.Replace('\\', '/') ?? "<none>";
        }

        var source = dictionary.Source?.OriginalString?.Replace('\\', '/');
        var themeMarker = source?.IndexOf("/Resources/Theme/", StringComparison.OrdinalIgnoreCase) ?? -1;
        if (themeMarker >= 0)
        {
            var normalizedSource = source![themeMarker..]
                .Replace("light.xaml", "{theme}.xaml", StringComparison.OrdinalIgnoreCase)
                .Replace("dark.xaml", "{theme}.xaml", StringComparison.OrdinalIgnoreCase);
            return $"Wpf.Ui{normalizedSource}";
        }

        return "Wpf.Ui/Resources/Theme/{theme}.xaml";
    }

    private static bool IsWpfUiThemeDictionary(ResourceDictionary dictionary)
    {
        if (dictionary is ThemesDictionary)
        {
            return true;
        }

        var source = dictionary.Source?.OriginalString?.Replace('\\', '/');
        return dictionary.GetType() == typeof(ResourceDictionary) &&
               source?.Contains("Wpf.Ui", StringComparison.OrdinalIgnoreCase) == true &&
               source.Contains("/Resources/Theme/", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeKey(object key) => key switch
    {
        Type type => $"type:{type.AssemblyQualifiedName}",
        _ => $"value:{key.GetType().AssemblyQualifiedName}:{key}"
    };

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

    private sealed record ProviderChainSnapshot(
        int DictionaryCount,
        string[] ProviderDictionarySignatures);

    private sealed class TestWindow : Window
    {
        public TestWindow(UIElement content)
        {
            Content = content;
            Width = 320;
            Height = 100;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.ToolWindow;
        }

        protected override void OnClosed(EventArgs e)
        {
            Keyboard.ClearFocus();
            base.OnClosed(e);
        }
    }
}
