using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NavigationViewItem = Wpf.Ui.Controls.NavigationViewItem;
using NavigationView = Wpf.Ui.Controls.NavigationView;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class Task5ComponentResourceTests
{
    [Fact]
    public void Task5_component_dictionaries_have_single_owners_and_are_merged_by_app()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var app = File.ReadAllText(Path.Combine(appRoot, "Bootstrap", "App.xaml"));
        var semantic = File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "SemanticStyles.xaml"));
        var inputs = File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "Components", "Inputs.xaml"));
        var lists = File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "Components", "ListsAndCards.xaml"));
        var navigation = File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "Components", "NavigationAndMenus.xaml"));
        var media = File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "Components", "MediaControls.xaml"));

        Assert.Contains("Components/Inputs.xaml", app);
        Assert.Contains("Components/ListsAndCards.xaml", app);
        Assert.Contains("Components/NavigationAndMenus.xaml", app);
        Assert.Contains("x:Key=\"InputTextBoxStyle\"", inputs);
        Assert.Contains("x:Key=\"InputPasswordBoxStyle\"", inputs);
        Assert.Contains("x:Key=\"InputComboBoxStyle\"", inputs);
        Assert.Contains("x:Key=\"NumberInputTextBoxStyle\"", inputs);
        Assert.Contains("x:Key=\"InputCheckBoxStyle\"", inputs);
        Assert.Contains("x:Key=\"InputToggleSwitchStyle\"", inputs);
        Assert.Contains("x:Key=\"InputErrorTextStyle\"", inputs);
        Assert.Contains("x:Key=\"InputHelpTextStyle\"", inputs);
        Assert.Contains("x:Key=\"RuleCardActionButtonStyle\"", File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "Components", "Buttons.xaml")));
        Assert.Contains("x:Key=\"VirtualizedListItemContainerStyle\"", lists);
        Assert.Contains("x:Key=\"SelectedCardContainerStyle\"", lists);
        Assert.Contains("x:Key=\"PlaybackProgressSliderStyle\"", media);
        Assert.Contains("x:Key=\"PlaybackProgressBarStyle\"", media);
        Assert.Contains("x:Key=\"AppMenuStyle\"", navigation);
        Assert.Contains("x:Key=\"AppContextMenuStyle\"", navigation);
        Assert.Contains("x:Key=\"AppToolTipStyle\"", navigation);
        Assert.DoesNotContain("PlaybackProgressSliderStyle", semantic);
        Assert.DoesNotContain("SelectedCardContainerStyle", semantic);
        Assert.DoesNotContain("SettingsNavigationRowButtonStyle", semantic);
    }

    [Fact]
    public void Input_resources_keep_height_focus_disabled_readonly_and_explicit_error_contracts()
    {
        WpfTestHost.RunInSta(() =>
        {
            var textBox = new TextBox
            {
                Style = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("InputTextBoxStyle")),
                ToolTip = "帮助说明"
            };
            textBox.ApplyTemplate();
            textBox.Measure(new Size(400, 100));
            textBox.Arrange(new Rect(0, 0, 400, textBox.DesiredSize.Height));
            textBox.UpdateLayout();

            Assert.Equal(36, textBox.Height);
            Assert.NotNull(textBox.ValidationErrorTemplate());
            Assert.Equal("帮助说明", textBox.ToolTip);
            Assert.Equal(new Thickness(1), textBox.BorderThickness);

            var inputSurface = Assert.IsType<Border>(textBox.Template.FindName("InputSurface", textBox));
            var focusRing = Assert.IsType<Border>(textBox.Template.FindName("KeyboardFocusRing", textBox));
            Assert.Equal(new CornerRadius(8), inputSurface.CornerRadius);
            Assert.Equal(Visibility.Collapsed, focusRing.Visibility);

            textBox.IsReadOnly = true;
            textBox.UpdateLayout();
            Assert.Equal(
                GetColor("SecondarySurfaceBrush"),
                Assert.IsType<SolidColorBrush>(inputSurface.Background).Color);

            textBox.IsEnabled = false;
            textBox.UpdateLayout();
            Assert.Equal(0.5, inputSurface.Opacity);

            var helpStyle = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("InputHelpTextStyle"));
            var errorStyle = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("InputErrorTextStyle"));
            Assert.Contains(helpStyle.Setters.OfType<Setter>(), setter => setter.Property == TextBlock.TextWrappingProperty);
            Assert.Contains(errorStyle.Setters.OfType<Setter>(), setter => setter.Property == TextBlock.ForegroundProperty);
        });
    }

    [Fact]
    public void Input_component_styles_are_consumed_by_implicit_controls_and_combo_item_containers()
    {
        WpfTestHost.RunInSta(() =>
        {
            new WpfUiThemeRuntime().ApplyLightTheme();

            var passwordBox = new PasswordBox();
            var checkBox = new CheckBox { Content = "启用" };
            var toggleSwitch = new ToggleSwitch { Content = "启用" };
            var comboBox = new ComboBox();
            comboBox.Items.Add("选项");

            var panel = new StackPanel();
            panel.Children.Add(passwordBox);
            panel.Children.Add(checkBox);
            panel.Children.Add(toggleSwitch);
            panel.Children.Add(comboBox);

            var window = new Window
            {
                Width = 480,
                Height = 360,
                Content = panel
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(36d, passwordBox.Height);
                Assert.Equal(HorizontalAlignment.Left, comboBox.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Center, comboBox.VerticalContentAlignment);
                Assert.Equal(
                    GetSetter<double>(
                        Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("InputCheckBoxStyle")),
                        FrameworkElement.MinHeightProperty),
                    checkBox.MinHeight);
                Assert.Equal(
                    GetSetter<double>(
                        Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("InputToggleSwitchStyle")),
                        FrameworkElement.MinHeightProperty),
                    toggleSwitch.MinHeight);

                Assert.Same(
                    global::System.Windows.Application.Current.FindResource("InputComboBoxItemStyle"),
                    comboBox.ItemContainerStyle);

                comboBox.IsDropDownOpen = true;
                window.UpdateLayout();
                var generatedItem = comboBox.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsType<ComboBoxItem>(generatedItem);
                Assert.Equal(VerticalAlignment.Center, ((ComboBoxItem)generatedItem).VerticalContentAlignment);
                Assert.Equal(
                    GetSetter<double>(
                        Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("InputComboBoxItemStyle")),
                        FrameworkElement.MinHeightProperty),
                    ((ComboBoxItem)generatedItem).MinHeight);
            }
            finally
            {
                comboBox.IsDropDownOpen = false;
                window.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_navigation_items_consume_the_shared_navigation_style_at_runtime()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            var window = provider.GetRequiredService<MainWindow>();
            var sharedStyle = Assert.IsType<Style>(
                global::System.Windows.Application.Current.FindResource("NavigationItemStyle"));

            try
            {
                window.Show();
                window.UpdateLayout();

                foreach (var name in new[]
                {
                    "LibraryNavigationItem",
                    "SettingsNavigationItem",
                    "ActiveCacheNavigationItem",
                    "PlaybackNavigationItem"
                })
                {
                    var item = Assert.IsType<NavigationViewItem>(window.FindName(name));
                    Assert.Same(sharedStyle, item.Style);
                    Assert.Equal(
                        GetSetter<double>(sharedStyle, FrameworkElement.MinHeightProperty),
                        item.MinHeight);
                }
            }
            finally
            {
                window.Close();
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Slider_progress_list_and_menu_resources_expose_observable_states()
    {
        WpfTestHost.RunInSta(() =>
        {
            var sliderStyle = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("PlaybackProgressSliderStyle"));
            var thumbStyle = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("PlaybackSliderThumbStyle"));
            var progressStyle = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("PlaybackProgressBarStyle"));
            var menuStyle = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("AppContextMenuStyle"));
            var tooltipStyle = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("AppToolTipStyle"));

            Assert.Equal(20d, GetSetter<double>(sliderStyle, Slider.HeightProperty));
            Assert.Equal(18d, GetSetter<double>(thumbStyle, Thumb.WidthProperty));
            Assert.Equal(0.35, GetSetter<double>(thumbStyle, UIElement.OpacityProperty));
            Assert.Equal(4d, GetSetter<double>(progressStyle, ProgressBar.HeightProperty));
            Assert.Contains(thumbStyle.Triggers.OfType<Trigger>(), trigger => trigger.Property == Thumb.IsDraggingProperty);
            Assert.Contains(thumbStyle.Triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsMouseOverProperty);
            Assert.Contains(menuStyle.Setters.OfType<Setter>(), setter => setter.Property == ItemsControl.ItemContainerStyleProperty);
            Assert.Contains(tooltipStyle.Setters.OfType<Setter>(), setter => setter.Property == Control.PaddingProperty);
        });

        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var lists = File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "Components", "ListsAndCards.xaml"));
        var cachePage = File.ReadAllText(Path.Combine(
            appRoot, "Features", "Cache", "CacheManagementPage.xaml"));
        var bookDetailsPage = File.ReadAllText(Path.Combine(
            appRoot, "Features", "BookDetails", "BookDetailsPage.xaml"));

        Assert.Contains("Condition Binding=\"{Binding IsMouseOver, RelativeSource={RelativeSource Self}}\"", lists);
        Assert.Contains("Condition Binding=\"{Binding IsSelected}\"", lists);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", cachePage);
        Assert.Contains("SelectionMode=\"Extended\"", cachePage);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", bookDetailsPage);
        Assert.Contains("AutomationProperties.Name", bookDetailsPage);
    }

    [Fact]
    public void Shared_layout_resources_keep_wpfui_controls_inheritable_and_compact()
    {
        WpfTestHost.RunInSta(() =>
        {
            new WpfUiThemeRuntime().ApplyLightTheme();
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MainWindow>();
                window.Show();
                window.UpdateLayout();
                var navigationItem = Assert.IsType<NavigationViewItem>(window.FindName("LibraryNavigationItem"));
                var navigationStyle = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("NavigationItemStyle"));
                Assert.NotNull(navigationStyle.BasedOn);
                Assert.Equal(new Thickness(0), navigationItem.Margin);
                var navigationView = Assert.IsType<NavigationView>(window.FindName("RootNavigationView"));
                Assert.Equal(0, navigationView.FrameMargin.Left);
                var settingsItem = Assert.IsType<NavigationViewItem>(window.FindName("SettingsNavigationItem"));
                var firstItemOrigin = navigationItem.TransformToAncestor(navigationView).Transform(new Point(0, 0));
                var secondItemOrigin = settingsItem.TransformToAncestor(navigationView).Transform(new Point(0, 0));
                Assert.InRange(
                    Math.Abs((secondItemOrigin.Y - firstItemOrigin.Y) - navigationItem.ActualHeight),
                    0d,
                    1d);

                var toggle = new ToggleSwitch { OffContent = "关闭", OnContent = "开启" };
                toggle.Style = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("InputToggleSwitchStyle"));
                var panel = new StackPanel();
                panel.Children.Add(toggle);
                var host = new Window { Width = 400, Height = 220, Content = panel };
                host.Show();
                host.UpdateLayout();
                Assert.Equal(96d, toggle.Width);
                Assert.Equal(96d, toggle.MinWidth);
                Assert.Equal(96d, toggle.ActualWidth);
                Assert.Equal(48d, toggle.ActualHeight);

                var combo = new ComboBox { Width = 220 };
                combo.Style = Assert.IsType<Style>(global::System.Windows.Application.Current.FindResource("InputComboBoxStyle"));
                panel.Children.Add(combo);
                host.UpdateLayout();
                Assert.Equal(0d, combo.MinWidth);
                Assert.Equal(220d, combo.ActualWidth);
            }
            finally
            {
                foreach (var diagnosticWindow in global::System.Windows.Application.Current.Windows.OfType<Window>().ToArray())
                {
                    diagnosticWindow.Close();
                }
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static T GetSetter<T>(Style style, DependencyProperty property)
    {
        var setter = style.Setters
            .OfType<Setter>()
            .Single(setter => setter.Property == property);
        return Assert.IsType<T>(setter.Value);
    }

    private static Color GetColor(string key) =>
        Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.FindResource(key)).Color;

    private static string GetRepositoryRoot()
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

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

internal static class ValidationExtensions
{
    public static ControlTemplate? ValidationErrorTemplate(this Control control) =>
        control.GetValue(Validation.ErrorTemplateProperty) as ControlTemplate;
}
