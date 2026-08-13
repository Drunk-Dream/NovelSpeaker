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
        "Provider.UiButton",
        "Provider.TextBox",
        "Provider.PasswordBox",
        "Provider.ComboBox",
        "Provider.ComboBoxItem",
        "Provider.CheckBox",
        "Provider.ToggleSwitch",
        "Provider.NavigationViewItem",
        "Provider.ProgressBar",
        "Provider.Slider",
        "Provider.Menu",
        "Provider.ContextMenu",
        "Provider.MenuItem",
        "Provider.Flyout",
        "Provider.Snackbar"
    ];

    private static readonly string[] StableApplicationResourceKeys =
    [
        "App.Typography.PageTitle",
        "App.Button.Primary",
        "App.Input.TextBox.Standard",
        "App.Progress.Standard",
        "App.Media.Slider",
        "App.Feedback.PopupSurface"
    ];

    [Fact]
    public void Bridge_styles_resolve_to_non_empty_provider_templates_in_both_themes()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var runtime = new WpfUiThemeRuntime();
            try
            {
                runtime.ApplyLightTheme();
                var before = CaptureProviderChain(application);

                var styles = BridgeKeys.ToDictionary(
                    key => key,
                    key => Assert.IsType<Style>(application.FindResource(key)),
                    StringComparer.Ordinal);
                Assert.All(styles.Values, style => Assert.NotNull(style.BasedOn));
                var stableResources = StableApplicationResourceKeys.ToDictionary(
                    key => key,
                    key => application.FindResource(key),
                    StringComparer.Ordinal);
                Assert.All(stableResources.Values, resource => Assert.True(resource is Style or ControlTemplate));

                foreach (var key in BridgeKeys)
                {
                    var control = CreateControl(key, styles[key]);
                    PrepareControl(control);
                    Assert.True(
                        control.Template is not null,
                        $"Provider alias did not resolve a template: {key}");
                }

                runtime.ApplyDarkTheme();
                foreach (var key in BridgeKeys)
                {
                    var control = CreateControl(key, styles[key]);
                    PrepareControl(control);
                    Assert.True(
                        control.Template is not null,
                        $"Provider alias did not resolve a dark-theme template: {key}");
                }
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
                Assert.All(
                    stableResources,
                    pair => Assert.Same(pair.Value, application.FindResource(pair.Key)));
            }
            finally
            {
                runtime.ApplyLightTheme();
            }
        });
    }

    private static Control CreateControl(string key, Style style) => key switch
    {
        "Provider.Button" => new WpfButton { Content = "button", Style = style },
        "Provider.UiButton" => new Wpf.Ui.Controls.Button { Content = "ui button", Style = style },
        "Provider.TextBox" => new WpfTextBox { Text = "text", Style = style },
        "Provider.PasswordBox" => new WpfPasswordBox { Password = "fixture", Style = style },
        "Provider.ComboBox" => new ComboBox
        {
            ItemsSource = new[] { "Light", "Dark" },
            SelectedIndex = 0,
            Style = style
        },
        "Provider.ComboBoxItem" => new ComboBoxItem { Content = "Provider combo item", Style = style },
        "Provider.CheckBox" => new CheckBox { Content = "check", Style = style },
        "Provider.ToggleSwitch" => new ToggleSwitch { Content = "toggle", Style = style },
        "Provider.Menu" => new System.Windows.Controls.Menu { Style = style },
        "Provider.ContextMenu" => new ContextMenu { Style = style },
        "Provider.MenuItem" => new System.Windows.Controls.MenuItem { Header = "menu", Style = style },
        "Provider.NavigationViewItem" => new NavigationViewItem { Content = "navigation", Style = style },
        "Provider.ProgressBar" => new ProgressBar { Minimum = 0, Maximum = 1, Value = 0.5, Style = style },
        "Provider.Slider" => new Slider { Minimum = 0, Maximum = 1, Value = 0.5, Style = style },
        "Provider.Flyout" => new Flyout { Content = "flyout", Style = style },
        "Provider.Snackbar" => new Snackbar(new SnackbarPresenter())
        {
            Title = "title",
            Content = "message",
            Style = style
        },
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
                WpfWindowHost.Show(window);
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
