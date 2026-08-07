using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
using NovelSpeaker.StyleGallery;
using Wpf.Ui.Controls;
using Xunit;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPasswordBox = System.Windows.Controls.PasswordBox;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class InputStyleContractTests
{
    private static readonly InputStyleContract[] InputStyles =
    [
        new("App.Input.TextBox.Standard", "{x:Type TextBox}", typeof(WpfTextBox), "Provider.TextBox"),
        new("App.Input.TextBox.Compact", "{x:Type TextBox}", typeof(WpfTextBox), "Provider.TextBox"),
        new("App.Input.PasswordBox.Standard", "{x:Type PasswordBox}", typeof(WpfPasswordBox), "Provider.PasswordBox"),
        new("App.Input.PasswordBox.Compact", "{x:Type PasswordBox}", typeof(WpfPasswordBox), "Provider.PasswordBox"),
        new("App.Input.ComboBox.Standard", "{x:Type ComboBox}", typeof(WpfComboBox), "Provider.ComboBox"),
        new("App.Input.ComboBox.Compact", "{x:Type ComboBox}", typeof(WpfComboBox), "Provider.ComboBox"),
        new("App.Input.CheckBox.Standard", "{x:Type CheckBox}", typeof(CheckBox), "Provider.CheckBox"),
        new("App.Input.CheckBox.Compact", "{x:Type CheckBox}", typeof(CheckBox), "Provider.CheckBox"),
        new("App.Input.ToggleSwitch.Standard", "{x:Type ui:ToggleSwitch}", typeof(ToggleSwitch), "Provider.ToggleSwitch"),
        new("App.Input.ToggleSwitch.Compact", "{x:Type ui:ToggleSwitch}", typeof(ToggleSwitch), "Provider.ToggleSwitch")
    ];

    private static readonly InputFixture[] InputFixtures =
    [
        new("input-textbox-empty-standard", "TextBox empty content"),
        new("input-textbox-long-standard", "TextBox long Chinese content"),
        new("input-textbox-readonly-compact", "TextBox read-only content"),
        new("input-textbox-disabled-compact", "TextBox disabled content"),
        new("input-textbox-error-standard", "TextBox invalid chapter name"),
        new("input-password-standard", "PasswordBox standard password"),
        new("input-password-disabled-compact", "PasswordBox disabled password"),
        new("input-combobox-options-standard", "ComboBox dropdown options"),
        new("input-combobox-long-compact", "ComboBox long selected item"),
        new("input-combobox-disabled-compact", "ComboBox disabled option"),
        new("input-checkbox-checked-standard", "CheckBox checked read chapter title"),
        new("input-checkbox-unchecked-standard", "CheckBox unchecked read footnotes"),
        new("input-checkbox-disabled-compact", "CheckBox disabled option"),
        new("input-checkbox-error-compact", "CheckBox invalid chapter title option"),
        new("input-toggle-labeled-on-standard", "ToggleSwitch on with label"),
        new("input-toggle-labeled-off-standard", "ToggleSwitch off with label"),
        new("input-toggle-unlabeled-on-compact", "ToggleSwitch on without label"),
        new("input-toggle-unlabeled-off-disabled-compact", "ToggleSwitch off disabled without label"),
        new("input-toggle-error-standard", "ToggleSwitch invalid background reading option")
    ];

    private static readonly IReadOnlyDictionary<string, string> ErrorMessages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["input-textbox-error-standard-error"] = "章节名称还需要包含作者信息。请补充后再继续。",
            ["input-checkbox-error-compact-error"] = "请选择是否合并章节标题，否则无法保存当前朗读配置。",
            ["input-toggle-error-standard-error"] = "后台朗读需要先启用本地缓存，请检查相关设置。"
        };

    [Fact]
    public void Input_style_dictionary_contains_all_named_variants_and_no_implicit_standard_styles()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Inputs.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var styles = document.Root?.Elements().Where(element => element.Name.LocalName == "Style").ToArray() ?? [];

        Assert.Equal(
            InputStyles.Select(style => style.Key).ToArray(),
            styles.Select(style => (string?)style.Attribute(xaml + "Key")).ToArray());
        Assert.All(InputStyles, expected =>
        {
            var style = styles.Single(resource => (string?)resource.Attribute(xaml + "Key") == expected.Key);
            Assert.Equal(expected.XamlTargetType, (string?)style.Attribute("TargetType"));
        });

        var standardControlNames = new[]
        {
            "Button",
            "CheckBox",
            "ComboBox",
            "ListBox",
            "ListBoxItem",
            "PasswordBox",
            "Slider",
            "TextBox",
            "ToggleButton",
            "ToggleSwitch"
        };
        var themingRoot = Path.Combine(LocateRepositoryRoot(), "src", "NovelSpeaker.App", "Shared", "Theming");
        var implicitStyles = Directory
            .EnumerateFiles(themingRoot, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(file => XDocument.Load(file)
                .Descendants()
                .Where(element => element.Name.LocalName == "Style" &&
                                  element.Attribute(xaml + "Key") is null &&
                                  standardControlNames.Any(name =>
                                      ((string?)element.Attribute("TargetType"))?.Contains(
                                          name,
                                          StringComparison.Ordinal) == true))
                .Select(element => Path.GetRelativePath(LocateRepositoryRoot(), file)))
            .ToArray();

        Assert.Empty(implicitStyles);
    }

    [Fact]
    public void Named_input_styles_resolve_through_the_provider_style_chain()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);

            foreach (var expected in InputStyles)
            {
                var style = Assert.IsType<Style>(application.FindResource(expected.Key));
                var provider = Assert.IsType<Style>(application.FindResource(expected.ProviderKey));
                Assert.Equal(expected.TargetType, style.TargetType);

                var chain = new List<Style>();
                for (var current = style; current is not null; current = current.BasedOn)
                {
                    Assert.True(chain.All(candidate => !ReferenceEquals(candidate, current)),
                        $"Style inheritance cycle detected at '{expected.Key}'.");
                    chain.Add(current);
                }

                Assert.Contains(chain, candidate => ReferenceEquals(candidate, provider));
            }
        });
    }

    [Fact]
    public void Input_controls_scene_contains_all_control_families_states_and_accessibility_metadata()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("input-controls");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var controls = FindInputControls(scene);
                Assert.Equal(InputFixtures.Length, controls.Count);
                Assert.Equal(
                    InputFixtures.Select(fixture => fixture.Id).Order(StringComparer.Ordinal),
                    controls.Select(control => AutomationProperties.GetAutomationId(control)).Order(StringComparer.Ordinal));
                Assert.All(InputFixtures, fixture =>
                {
                    var control = controls.Single(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == fixture.Id);
                    Assert.Equal(fixture.Name, AutomationProperties.GetName(control));
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control)));
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
                });

                Assert.Equal(5, controls.OfType<WpfTextBox>().Count());
                Assert.Equal(2, controls.OfType<WpfPasswordBox>().Count());
                Assert.Equal(3, controls.OfType<WpfComboBox>().Count());
                Assert.Equal(4, controls.OfType<CheckBox>().Count());
                Assert.Equal(5, controls.OfType<ToggleSwitch>().Count());

                var emptyTextBox = GetControl<WpfTextBox>(controls, "input-textbox-empty-standard");
                Assert.Empty(emptyTextBox.Text);
                Assert.Contains("长中文内容", GetControl<WpfTextBox>(controls, "input-textbox-long-standard").Text);
                Assert.True(GetControl<WpfTextBox>(controls, "input-textbox-readonly-compact").IsReadOnly);
                Assert.False(GetControl<WpfTextBox>(controls, "input-textbox-disabled-compact").IsEnabled);
                Assert.True(Validation.GetHasError(
                    GetControl<WpfTextBox>(controls, "input-textbox-error-standard")));

                Assert.Equal("gallery-secret", GetControl<WpfPasswordBox>(controls, "input-password-standard").Password);
                Assert.False(GetControl<WpfPasswordBox>(controls, "input-password-disabled-compact").IsEnabled);

                Assert.Equal(0, GetControl<WpfComboBox>(controls, "input-combobox-options-standard").SelectedIndex);
                Assert.Equal(1, GetControl<WpfComboBox>(controls, "input-combobox-long-compact").SelectedIndex);
                Assert.False(GetControl<WpfComboBox>(controls, "input-combobox-disabled-compact").IsEnabled);

                Assert.True(GetControl<CheckBox>(controls, "input-checkbox-checked-standard").IsChecked is true);
                Assert.True(GetControl<CheckBox>(controls, "input-checkbox-unchecked-standard").IsChecked is false);
                Assert.False(GetControl<CheckBox>(controls, "input-checkbox-disabled-compact").IsEnabled);
                Assert.True(Validation.GetHasError(
                    GetControl<CheckBox>(controls, "input-checkbox-error-compact")));

                Assert.True(GetControl<ToggleSwitch>(controls, "input-toggle-labeled-on-standard").IsChecked is true);
                Assert.True(GetControl<ToggleSwitch>(controls, "input-toggle-labeled-off-standard").IsChecked is false);
                Assert.Null(GetControl<ToggleSwitch>(controls, "input-toggle-unlabeled-on-compact").Content);
                Assert.Null(GetControl<ToggleSwitch>(controls, "input-toggle-unlabeled-off-disabled-compact").Content);
                Assert.False(GetControl<ToggleSwitch>(controls, "input-toggle-unlabeled-off-disabled-compact").IsEnabled);
                Assert.True(Validation.GetHasError(
                    GetControl<ToggleSwitch>(controls, "input-toggle-error-standard")));

                var errorBlocks = FindDescendants<WpfTextBlock>(scene)
                    .Where(block => AutomationProperties.GetAutomationId(block).EndsWith(
                        "-error",
                        StringComparison.Ordinal))
                    .ToDictionary(
                        block => AutomationProperties.GetAutomationId(block),
                        block => block.Text,
                        StringComparer.Ordinal);
                Assert.Equal(ErrorMessages.Count, errorBlocks.Count);
                Assert.All(ErrorMessages, expected => Assert.Equal(expected.Value, errorBlocks[expected.Key]));
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Every_input_control_has_nonzero_layout_and_unlabeled_toggles_keep_their_minimum_width(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("input-controls");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var controls = FindInputControls(scene);
                Assert.All(controls, control =>
                {
                    Assert.True(IsFiniteAndPositive(control.ActualWidth),
                        $"{AutomationProperties.GetAutomationId(control)} width was not usable.");
                    Assert.True(IsFiniteAndPositive(control.ActualHeight),
                        $"{AutomationProperties.GetAutomationId(control)} height was not usable.");
                });

                foreach (var toggle in controls.OfType<ToggleSwitch>())
                {
                    Assert.True(toggle.MinWidth > 0);
                    Assert.True(
                        toggle.ActualWidth + 0.01 >= toggle.MinWidth,
                        $"{AutomationProperties.GetAutomationId(toggle)} collapsed below its minimum width.");
                }
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    [Fact]
    public void Input_controls_keep_style_and_template_instances_while_dynamic_colors_follow_theme_switches()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("input-controls");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var controls = FindInputControls(scene).ToDictionary(
                    control => AutomationProperties.GetAutomationId(control),
                    StringComparer.Ordinal);
                var light = controls.ToDictionary(
                    pair => pair.Key,
                    pair => CaptureVisualState(pair.Value),
                    StringComparer.Ordinal);
                Assert.All(light, pair =>
                {
                    Assert.NotNull(pair.Value.Style);
                    Assert.NotNull(pair.Value.Template);
                    Assert.NotEmpty(pair.Value.Colors);
                });

                GalleryThemeRuntime.Apply(GalleryTheme.Dark);
                host.Window.UpdateLayout();

                Assert.All(light, pair =>
                {
                    var current = controls[pair.Key];
                    var dark = CaptureVisualState(current);
                    Assert.Same(pair.Value.Style, dark.Style);
                    Assert.Same(pair.Value.Template, dark.Template);
                    Assert.Equal(pair.Value.Colors.Keys.Order(StringComparer.Ordinal), dark.Colors.Keys.Order(StringComparer.Ordinal));
                    Assert.Contains(
                        pair.Value.Colors,
                        color => dark.Colors[color.Key] != color.Value);
                });
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void ComboBox_popup_and_items_construct_with_resolvable_theme_resources(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("input-controls");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            var combo = GetControl<WpfComboBox>(FindInputControls(scene), "input-combobox-options-standard");
            try
            {
                host.Window.UpdateLayout();
                combo.ApplyTemplate();
                var popup = Assert.IsType<Popup>(FindComboBoxPopup(combo));
                combo.IsDropDownOpen = true;
                host.Window.UpdateLayout();

                Assert.True(popup.IsOpen);
                Assert.NotNull(popup.Child);
                Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
                var item = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
                item.ApplyTemplate();
                Assert.NotNull(item.Template);

                foreach (var key in new[]
                         {
                             "App.Brush.Surface.Primary",
                             "App.Brush.Text.Primary",
                             "App.Brush.Border.Subtle",
                             "App.Brush.Focus"
                         })
                {
                    Assert.IsType<SolidColorBrush>(combo.FindResource(key));
                    Assert.IsType<SolidColorBrush>(item.FindResource(key));
                }

                Assert.IsType<SolidColorBrush>(combo.Background);
                Assert.IsType<SolidColorBrush>(combo.Foreground);
                Assert.NotNull(item.FindResource("App.Brush.Text.Primary"));
            }
            finally
            {
                combo.IsDropDownOpen = false;
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void ComboBox_styles_keep_stretch_alignment_and_full_surface_toggle_target(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("input-controls");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var combos = FindInputControls(scene).OfType<WpfComboBox>();
                Assert.Equal(3, combos.Count());
                foreach (var combo in combos)
                {
                    combo.ApplyTemplate();
                    Assert.Equal(HorizontalAlignment.Stretch, combo.HorizontalContentAlignment);
                    Assert.True(IsFiniteAndPositive(combo.ActualWidth));

                    var toggle = Assert.Single(FindDescendants<ToggleButton>(combo));
                    Assert.True(
                        toggle.ActualWidth >= combo.ActualWidth - 2.0,
                        $"{AutomationProperties.GetAutomationId(combo)} toggle surface was smaller than the control.");
                    Assert.True(
                        toggle.ActualHeight >= combo.ActualHeight - 8.0,
                        $"{AutomationProperties.GetAutomationId(combo)} toggle surface was shorter than the control.");

                    var toggleBounds = toggle.TransformToAncestor(combo)
                        .TransformBounds(new Rect(new Point(), toggle.RenderSize));
                    Assert.True(
                        combo.ActualWidth - toggleBounds.Right <= 2.0,
                        $"{AutomationProperties.GetAutomationId(combo)} toggle did not reach the right edge.");
                }
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void ComboBox_long_string_item_trims_on_one_line_without_moving_the_chevron(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("input-controls");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var combo = GetControl<WpfComboBox>(FindInputControls(scene), "input-combobox-long-compact");
                combo.ApplyTemplate();

                var text = Assert.Single(
                    FindDescendants<WpfTextBlock>(combo),
                    block => block.Text.Contains("长中文选项", StringComparison.Ordinal));
                Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
                Assert.Equal(HorizontalAlignment.Stretch, text.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, text.VerticalAlignment);
                Assert.True(IsFiniteAndPositive(text.ActualWidth));
                Assert.True(
                    text.ActualHeight <= combo.ActualHeight,
                    $"{AutomationProperties.GetAutomationId(combo)} long selected item wrapped to more than one line.");

                var textBounds = text.TransformToAncestor(combo)
                    .TransformBounds(new Rect(new Point(), text.RenderSize));
                Assert.True(
                    textBounds.Right <= combo.ActualWidth - 1.0,
                    $"{AutomationProperties.GetAutomationId(combo)} long selected item overflowed the closed control.");

                var chevron = Assert.Single(FindDescendants<SymbolIcon>(combo));
                var chevronBounds = chevron.TransformToAncestor(combo)
                    .TransformBounds(new Rect(new Point(), chevron.RenderSize));
                Assert.True(
                    textBounds.Right <= chevronBounds.Left,
                    $"{AutomationProperties.GetAutomationId(combo)} selected text overlapped the chevron area.");

                var reference = GetControl<WpfComboBox>(FindInputControls(scene), "input-combobox-options-standard");
                reference.ApplyTemplate();
                var referenceChevron = Assert.Single(FindDescendants<SymbolIcon>(reference));
                var referenceBounds = referenceChevron.TransformToAncestor(reference)
                    .TransformBounds(new Rect(new Point(), referenceChevron.RenderSize));
                Assert.True(
                    Math.Abs(chevronBounds.Right - referenceBounds.Right) <= 0.5,
                    $"{AutomationProperties.GetAutomationId(combo)} long item moved the chevron.");
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void ComboBox_popup_is_never_narrower_than_the_closed_control(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("input-controls");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            var combo = GetControl<WpfComboBox>(FindInputControls(scene), "input-combobox-options-standard");
            try
            {
                host.Window.UpdateLayout();
                combo.ApplyTemplate();
                var closedWidth = combo.ActualWidth;
                Assert.True(IsFiniteAndPositive(closedWidth));

                var popup = Assert.IsType<Popup>(FindComboBoxPopup(combo));
                combo.IsDropDownOpen = true;
                host.Window.UpdateLayout();

                Assert.True(popup.IsOpen);
                var popupChild = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
                Assert.True(
                    IsFiniteAndPositive(popupChild.ActualWidth) &&
                    popupChild.ActualWidth >= closedWidth - 0.5,
                    $"ComboBox popup content ({popupChild.ActualWidth:F1}) was narrower than the closed control ({closedWidth:F1}).");
                var firstItem = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.True(
                    IsFiniteAndPositive(firstItem.ActualWidth) &&
                    firstItem.ActualWidth >= closedWidth - 2.0,
                    $"ComboBox popup items ({firstItem.ActualWidth:F1}) were narrower than the closed control ({closedWidth:F1}).");
            }
            finally
            {
                combo.IsDropDownOpen = false;
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void ComboBox_disabled_string_item_keeps_state_foreground_without_double_opacity(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("input-controls");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var combo = GetControl<WpfComboBox>(FindInputControls(scene), "input-combobox-disabled-compact");
                combo.ApplyTemplate();
                Assert.False(combo.IsEnabled);

                var text = Assert.Single(
                    FindDescendants<WpfTextBlock>(combo),
                    block => block.Text == "普通章节");
                Assert.Equal(1.0, text.Opacity);
                Assert.Equal(
                    ((SolidColorBrush)combo.Foreground).Color,
                    ((SolidColorBrush)text.Foreground).Color);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    private static IReadOnlyList<Control> FindInputControls(DependencyObject root) =>
        FindDescendants<Control>(root)
            .Where(control => control is WpfTextBox or
                              WpfPasswordBox or
                              WpfComboBox or
                              CheckBox or
                              ToggleSwitch)
            .Where(control => InputFixtures.Any(fixture =>
                fixture.Id.Equals(
                    AutomationProperties.GetAutomationId(control),
                    StringComparison.Ordinal)))
            .ToArray();

    private static T GetControl<T>(IEnumerable<Control> controls, string automationId)
        where T : Control =>
        Assert.IsType<T>(controls.Single(control =>
            AutomationProperties.GetAutomationId(control) == automationId));

    private static InputVisualState CaptureVisualState(Control control)
    {
        control.ApplyTemplate();
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var (name, property) in new[]
                 {
                     ("Foreground", Control.ForegroundProperty),
                     ("Background", Control.BackgroundProperty),
                     ("BorderBrush", Control.BorderBrushProperty)
                 })
        {
            if (control.GetValue(property) is SolidColorBrush brush)
            {
                colors[name] = brush.Color;
            }
        }

        return new InputVisualState(control.Style, control.Template, colors);
    }

    private static Popup? FindComboBoxPopup(WpfComboBox combo)
    {
        foreach (var name in new[] { "PART_Popup", "PART_PopupTP", "Popup" })
        {
            if (combo.Template?.FindName(name, combo) is Popup popup)
            {
                return popup;
            }
        }

        return FindDescendants<Popup>(combo).SingleOrDefault();
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

    private static bool IsFiniteAndPositive(double value) => double.IsFinite(value) && value > 0;

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

    private sealed record InputStyleContract(
        string Key,
        string XamlTargetType,
        Type TargetType,
        string ProviderKey);

    private sealed record InputFixture(string Id, string Name);

    private sealed record InputVisualState(
        Style? Style,
        ControlTemplate? Template,
        IReadOnlyDictionary<string, Color> Colors);
}
