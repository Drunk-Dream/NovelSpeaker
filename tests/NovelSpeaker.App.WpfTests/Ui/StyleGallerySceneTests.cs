using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using NovelSpeaker.StyleGallery;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class StyleGallerySceneTests
{
    [Fact]
    public void Scene_registry_groups_concrete_gallery_scenes_with_fixed_dimensions()
    {
        var scenes = GallerySceneRegistry.All;

        Assert.Equal(
            ["button-styles", "input-controls", "list-components", "media-controls", "navigation-feedback", "palette-probe", "provider-controls", "provider-style-probe", "surfaces", "theme-resource-probe", "token-components", "typography"],
            scenes.Select(scene => scene.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Theme foundations", "Standard controls", "Component families"],
            scenes.Select(scene => scene.GroupName).Distinct(StringComparer.Ordinal));
        Assert.Equal(
            ["provider-style-probe", "theme-resource-probe", "palette-probe", "token-components", "typography", "surfaces"],
            scenes.Where(scene => scene.Group == GallerySceneGroup.ThemeFoundations)
                .Select(scene => scene.Name));
        Assert.Equal(
            ["provider-controls", "button-styles", "input-controls"],
            scenes.Where(scene => scene.Group == GallerySceneGroup.StandardControls)
                .Select(scene => scene.Name));
        Assert.Equal(
            ["media-controls", "list-components", "navigation-feedback"],
            scenes.Where(scene => scene.Group == GallerySceneGroup.ComponentFamilies)
                .Select(scene => scene.Name));
        Assert.DoesNotContain(scenes, scene => scene.Name == "placeholder-sections");
        Assert.All(scenes, scene =>
        {
            Assert.Equal(GalleryRenderSettings.WindowWidth, scene.Width);
            Assert.Equal(GalleryRenderSettings.WindowHeight, scene.Height);
        });
        Assert.Equal(96, GalleryRenderSettings.Dpi);
    }

    [Fact]
    public void Gallery_window_scene_selector_exposes_the_three_scene_groups()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            var window = new GalleryWindow();
            var selector = FindDescendants<ComboBox>((DependencyObject)window.Content!).Single();
            var view = Assert.IsAssignableFrom<ICollectionView>(selector.ItemsSource);

            Assert.Equal(
                ["Theme foundations", "Standard controls", "Component families"],
                view.Groups.Cast<CollectionViewGroup>().Select(group => group.Name));
            Assert.Equal(12, view.Cast<GallerySceneDefinition>().Count());

            var headerTemplate = Assert.Single(selector.GroupStyle).HeaderTemplate;
            Assert.NotNull(headerTemplate);
            Assert.Equal(
                ["Theme foundations", "Standard controls", "Component families"],
                view.Groups.Cast<CollectionViewGroup>()
                    .Select(group =>
                    {
                        var headerHost = new ContentControl
                        {
                            Content = group,
                            ContentTemplate = headerTemplate!
                        };
                        headerHost.Measure(new Size(240, 32));
                        headerHost.Arrange(new Rect(0, 0, 240, 32));
                        headerHost.UpdateLayout();
                        var header = FindDescendants<WpfTextBlock>(headerHost).Single();
                        return header.Text;
                    }));
        });
    }

    [Fact]
    public void Gallery_screenshot_options_use_stable_scene_ids_and_family_outputs()
    {
        var defaultOptions = GalleryCommandLineOptions.Parse(["--screenshot"]);
        var sceneOptions = GalleryCommandLineOptions.Parse(
        [
            "--screenshot",
            "--scene",
            "button-styles",
            "--output",
            Path.Combine("artifacts", "visual-review", "gallery", "buttons")
        ]);

        Assert.Equal(
            Path.Combine("artifacts", "visual-review", "gallery"),
            defaultOptions.OutputDirectory);
        Assert.Null(defaultOptions.SceneName);
        Assert.Equal("button-styles", sceneOptions.SceneName);
        Assert.Equal(
            Path.Combine("artifacts", "visual-review", "gallery", "buttons"),
            sceneOptions.OutputDirectory);
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Every_scene_can_measure_arrange_and_render_without_dispatcher_exceptions(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var dispatcherExceptions = new List<Exception>();
            DispatcherUnhandledExceptionEventHandler handler = (_, args) =>
            {
                dispatcherExceptions.Add(args.Exception);
                args.Handled = true;
            };
            Dispatcher.CurrentDispatcher.UnhandledException += handler;
            try
            {
                foreach (var scene in GallerySceneRegistry.All)
                {
                    var root = scene.Create();
                    Assert.Equal(scene.Name, AutomationProperties.GetAutomationId(root));

                    root.Measure(new Size(scene.Width, scene.Height));
                    root.Arrange(new Rect(0, 0, scene.Width, scene.Height));
                    root.UpdateLayout();

                    var bitmap = GallerySceneRenderer.Render(root, scene);

                    Assert.Equal(scene.Width, bitmap.PixelWidth);
                    Assert.Equal(scene.Height, bitmap.PixelHeight);
                    Assert.True(bitmap.DpiX > 0);
                    Assert.True(bitmap.DpiY > 0);
                    Assert.True(root.ActualWidth > 0);
                    Assert.True(root.ActualHeight > 0);
                }
            }
            finally
            {
                Dispatcher.CurrentDispatcher.UnhandledException -= handler;
            }

            Assert.Empty(dispatcherExceptions);
        });
    }

    [Fact]
    public void Provider_scene_contains_provider_controls_and_theme_probe_has_dynamic_resources()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            var providerScene = GallerySceneRegistry.Build("provider-controls");
            var probeScene = GallerySceneRegistry.Build("theme-resource-probe");

            Assert.NotEmpty(FindDescendants<WpfButton>(providerScene));
            Assert.NotEmpty(FindDescendants<WpfTextBox>(providerScene));
            Assert.NotEmpty(FindDescendants<ComboBox>(providerScene));
            Assert.NotEmpty(FindDescendants<ToggleSwitch>(providerScene));
            Assert.NotEmpty(FindDescendants<Slider>(providerScene));
            Assert.NotEmpty(FindDescendants<ProgressBar>(providerScene));

            var bridgeProbe = GallerySceneRegistry.Build("provider-style-probe");
            bridgeProbe.Measure(new Size(GalleryRenderSettings.WindowWidth, GalleryRenderSettings.WindowHeight));
            bridgeProbe.Arrange(new Rect(0, 0, GalleryRenderSettings.WindowWidth, GalleryRenderSettings.WindowHeight));
            bridgeProbe.UpdateLayout();
            var bridgeControls = FindDescendants<Control>(bridgeProbe)
                .Where(control => AutomationProperties.GetName(control).StartsWith(
                    "Provider.",
                    StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(18, bridgeControls.Length);
            Assert.NotEmpty(FindDescendants<Wpf.Ui.Controls.Button>(bridgeProbe));
            Assert.All(
                bridgeControls,
                control => Assert.NotNull(control.Template));

            var dynamicResourceElements = FindDescendants<FrameworkElement>(probeScene)
                .Where(element => element.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue ||
                                  element.ReadLocalValue(Control.ForegroundProperty) != DependencyProperty.UnsetValue)
                .ToArray();
            Assert.NotEmpty(dynamicResourceElements);
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Button_style_gallery_contains_named_variants_states_and_content_fixtures(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("button-styles");
            var host = new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(host);
                host.UpdateLayout();

                var buttons = FindDescendants<WpfButton>(scene)
                    .Where(button => AutomationProperties.GetAutomationId(button).StartsWith(
                        "button-",
                        StringComparison.Ordinal))
                    .ToArray();
                Assert.Equal(42, buttons.Length);
                Assert.All(
                    buttons,
                    button =>
                    {
                        Assert.NotNull(button.Style);
                        Assert.StartsWith("App.Button.", AutomationProperties.GetName(button));
                        Assert.True(button.ActualWidth >= 32);
                        Assert.True(button.ActualHeight >= 32);
                        Assert.True(button.IsEnabled || button.Opacity < 1);
                    });

                foreach (var variant in new[]
                         {
                             "primary",
                             "secondary",
                             "subtle",
                             "icon",
                             "danger",
                             "dangericon",
                             "toolbarvalue",
                             "floating"
                         })
                {
                    var stateButtons = buttons
                        .Where(button =>
                        {
                            var automationId = AutomationProperties.GetAutomationId(button);
                            return new[] { "default", "hover", "pressed", "focus", "disabled" }
                                .Any(state => automationId == $"button-{variant}-{state}");
                        })
                        .ToArray();
                    Assert.Equal(5, stateButtons.Length);
                    var defaultButton = stateButtons.Single(button =>
                        AutomationProperties.GetAutomationId(button) == $"button-{variant}-default");
                    Assert.All(
                        stateButtons,
                        stateButton =>
                        {
                            Assert.Equal(defaultButton.ActualWidth, stateButton.ActualWidth);
                            Assert.Equal(defaultButton.ActualHeight, stateButton.ActualHeight);
                        });
                }

                Assert.NotNull(FindDescendants<WpfTextBlock>(scene).Single(block =>
                    block.Text.StartsWith("长中文文本：", StringComparison.Ordinal)));
                Assert.NotNull(FindDescendants<WpfTextBlock>(scene).Single(block =>
                    block.Text == "图标 + 文本"));
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Button_style_gallery_binds_symbol_icons_to_theme_semantic_foregrounds(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("button-styles");
            var host = new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(host);
                host.UpdateLayout();

                var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                    global::System.Windows.Application.Current);
                var iconButton = FindDescendants<WpfButton>(scene).Single(button =>
                    AutomationProperties.GetAutomationId(button) == "button-icon-default");
                var icon = FindDescendants<SymbolIcon>(iconButton).Single();
                var iconAndText = FindDescendants<WpfButton>(scene).Single(button =>
                    AutomationProperties.GetAutomationId(button) == "button-icon-text");
                var iconAndTextSymbol = FindDescendants<SymbolIcon>(iconAndText).Single();

                var expectedIconBrush = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Text.Primary"));
                var expectedPrimaryBrush = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Accent.Text"));
                Assert.Equal(expectedIconBrush.Color, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
                Assert.Equal(
                    expectedPrimaryBrush.Color,
                    Assert.IsType<SolidColorBrush>(iconAndTextSymbol.Foreground).Color);
                Assert.NotEqual(
                    Colors.Black,
                    Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
                Assert.NotEqual(
                    Colors.Black,
                    Assert.IsType<SolidColorBrush>(iconAndTextSymbol.Foreground).Color);

                Assert.Equal(
                    expectedIconBrush.Color,
                    Assert.IsType<SolidColorBrush>(icon.GetValue(TextElement.ForegroundProperty)).Color);
                Assert.Equal(
                    expectedPrimaryBrush.Color,
                    Assert.IsType<SolidColorBrush>(
                        iconAndTextSymbol.GetValue(TextElement.ForegroundProperty)).Color);

                var iconGlyph = Assert.Single(FindDescendants<WpfTextBlock>(icon));
                var iconAndTextGlyph = Assert.Single(FindDescendants<WpfTextBlock>(iconAndTextSymbol));
                Assert.Equal(expectedIconBrush.Color, Assert.IsType<SolidColorBrush>(iconGlyph.Foreground).Color);
                Assert.Equal(
                    expectedPrimaryBrush.Color,
                    Assert.IsType<SolidColorBrush>(iconAndTextGlyph.Foreground).Color);
                var primaryBackground = Assert.IsType<SolidColorBrush>(iconAndText.Background);
                Assert.True(
                    ContrastRatio(
                        Assert.IsType<SolidColorBrush>(iconAndTextGlyph.Foreground).Color,
                        primaryBackground.Color) >= 4.4,
                    $"Primary icon contrast was {ContrastRatio(
                        Assert.IsType<SolidColorBrush>(iconAndTextGlyph.Foreground).Color,
                        primaryBackground.Color):0.00}:1.");
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Button_style_gallery_danger_pressed_uses_a_readable_semantic_color_pair(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("button-styles");
            var host = new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(host);
                host.UpdateLayout();

                var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                    global::System.Windows.Application.Current);
                var pressed = FindDescendants<WpfButton>(scene).Single(button =>
                    AutomationProperties.GetAutomationId(button) == "button-danger-pressed");
                var foreground = Assert.IsType<SolidColorBrush>(pressed.Foreground);
                var background = Assert.IsType<SolidColorBrush>(pressed.Background);
                var expectedForeground = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Danger.Pressed.Text"));
                var expectedBackground = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Danger.Pressed"));
                var dangerIconHover = FindDescendants<WpfButton>(scene).Single(button =>
                    AutomationProperties.GetAutomationId(button) == "button-dangericon-hover");
                var expectedDangerIconHoverBackground = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Danger"));
                var expectedDangerIconHoverForeground = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Danger.Text"));

                Assert.Equal(expectedForeground.Color, foreground.Color);
                Assert.Equal(expectedBackground.Color, background.Color);
                Assert.True(
                    ContrastRatio(foreground.Color, background.Color) >= 4.5,
                    $"Danger Pressed contrast was {ContrastRatio(foreground.Color, background.Color):0.00}:1.");
                Assert.Equal(
                    expectedDangerIconHoverBackground.Color,
                    Assert.IsType<SolidColorBrush>(dangerIconHover.Background).Color);
                Assert.Equal(
                    expectedDangerIconHoverForeground.Color,
                    Assert.IsType<SolidColorBrush>(dangerIconHover.Foreground).Color);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Button_style_gallery_keeps_layout_sizes_when_theme_changes()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("button-styles");
            var host = new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(host);
                host.UpdateLayout();
                var buttons = FindDescendants<WpfButton>(scene)
                    .Where(button => AutomationProperties.GetAutomationId(button).StartsWith(
                        "button-",
                        StringComparison.Ordinal))
                    .ToDictionary(
                        button => AutomationProperties.GetAutomationId(button),
                        button => (button.Style, button.ActualWidth, button.ActualHeight),
                        StringComparer.Ordinal);
                var lightBackground = Assert.IsType<SolidColorBrush>(
                    FindDescendants<WpfButton>(scene).Single(button =>
                        AutomationProperties.GetAutomationId(button) == "button-primary-default").Background);
                var dynamicStateIds = new[]
                {
                    "button-primary-hover",
                    "button-primary-pressed",
                    "button-primary-focus",
                    "button-dangericon-hover"
                };
                var lightStateColors = dynamicStateIds.ToDictionary(
                    automationId => automationId,
                    automationId => Assert.IsType<SolidColorBrush>(
                        FindDescendants<WpfButton>(scene).Single(button =>
                            AutomationProperties.GetAutomationId(button) == automationId).Background).Color,
                    StringComparer.Ordinal);

                GalleryThemeRuntime.Apply(GalleryTheme.Dark);
                host.UpdateLayout();

                Assert.All(buttons, pair =>
                {
                    var button = FindDescendants<WpfButton>(scene).Single(candidate =>
                        AutomationProperties.GetAutomationId(candidate) == pair.Key);
                    Assert.Same(pair.Value.Style, button.Style);
                    Assert.Equal(pair.Value.ActualWidth, button.ActualWidth);
                    Assert.Equal(pair.Value.ActualHeight, button.ActualHeight);
                });
                Assert.NotEqual(
                    lightBackground.Color,
                    Assert.IsType<SolidColorBrush>(
                        FindDescendants<WpfButton>(scene).Single(button =>
                            AutomationProperties.GetAutomationId(button) == "button-primary-default").Background).Color);
                Assert.All(
                    lightStateColors,
                    pair => Assert.NotEqual(
                        pair.Value,
                        Assert.IsType<SolidColorBrush>(
                            FindDescendants<WpfButton>(scene).Single(button =>
                                AutomationProperties.GetAutomationId(button) == pair.Key).Background).Color));
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                host.Close();
            }
        });
    }

    [Fact]
    public void Palette_probe_updates_dynamic_brushes_without_replacing_style_or_template()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("palette-probe");
            var host = new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(host);
                host.UpdateLayout();

                var swatches = FindDescendants<Border>(scene)
                    .Where(border => AutomationProperties.GetAutomationId(border).StartsWith(
                        "palette-",
                        StringComparison.Ordinal))
                    .ToDictionary(
                        border => AutomationProperties.GetAutomationId(border),
                        border => Assert.IsType<SolidColorBrush>(border.Background),
                        StringComparer.Ordinal);
                var canonicalBrushKeys = NovelSpeaker.App.Shared.Theming.SemanticPaletteRuntime.Keys
                    .Where(key => key.StartsWith("App.Brush.", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(28, canonicalBrushKeys.Length);
                Assert.Equal(
                    canonicalBrushKeys.Select(key => $"palette-{key}").Order(StringComparer.Ordinal),
                    swatches.Keys.Order(StringComparer.Ordinal));

                var contrastSamples = FindDescendants<Border>(scene)
                    .Where(border => AutomationProperties.GetAutomationId(border).StartsWith(
                        "contrast-sample-",
                        StringComparison.Ordinal))
                    .ToArray();
                Assert.Equal(5, contrastSamples.Length);
                Assert.All(contrastSamples, sample => Assert.NotNull(sample.Background));

                var providerStyle = Assert.IsType<Style>(application.FindResource("Provider.Button"));
                var button = new WpfButton
                {
                    Content = "template stability fixture",
                    Style = providerStyle
                };
                button.Measure(new Size(240, 60));
                button.Arrange(new Rect(0, 0, 240, 60));
                button.ApplyTemplate();
                button.UpdateLayout();
                var template = button.Template;
                Assert.NotNull(template);

                var lightColors = swatches.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Color,
                    StringComparer.Ordinal);

                GalleryThemeRuntime.Apply(GalleryTheme.Dark);
                host.UpdateLayout();

                Assert.All(
                    swatches,
                    pair =>
                    {
                        var current = Assert.IsType<SolidColorBrush>(
                            FindDescendants<Border>(scene).Single(border =>
                                AutomationProperties.GetAutomationId(border) == pair.Key).Background);
                        Assert.NotEqual(lightColors[pair.Key], current.Color);
                    });
                Assert.Same(providerStyle, application.FindResource("Provider.Button"));
                Assert.Same(template, button.Template);

                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Token_components_measure_and_arrange_at_supported_dpi_scales(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("token-components");

            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                // The shared WPF host runs at its machine DPI. A layout transform
                // provides a deterministic physical-scale contract for 100/125/150%.
                scene.LayoutTransform = new ScaleTransform(scale, scale);
                var availableSize = new Size(
                    GalleryRenderSettings.WindowWidth,
                    GalleryRenderSettings.WindowHeight);
                scene.Measure(availableSize);
                scene.Arrange(new Rect(new Point(0, 0), availableSize));
                scene.UpdateLayout();

                foreach (var componentId in new[]
                         {
                             "component-page-header",
                             "component-section-surface",
                             "component-status-view",
                             "component-status-view-success",
                             "component-status-view-warning",
                             "component-status-view-error"
                         })
                {
                    var component = FindDescendants<FrameworkElement>(scene)
                        .Single(element => AutomationProperties.GetAutomationId(element) == componentId);
                    Assert.True(
                        IsFiniteAndPositive(component.ActualWidth) && IsFiniteAndPositive(component.ActualHeight),
                        $"{componentId} did not receive a usable layout at {scale:0.##}x DPI.");
                }

                foreach (var textId in new[]
                         {
                             "component-page-header-title",
                             "component-page-header-description",
                             "component-section-surface-title",
                             "component-section-surface-body",
                             "component-status-view-error-description"
                         })
                {
                    var text = FindDescendants<WpfTextBlock>(scene)
                        .Single(block => AutomationProperties.GetAutomationId(block) == textId);
                    Assert.True(
                        IsFiniteAndPositive(text.ActualWidth) && IsFiniteAndPositive(text.ActualHeight),
                        $"{textId} was clipped to an unusable layout at {scale:0.##}x DPI.");
                    Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
                }

                Assert.All(
                    FindDescendants<FrameworkElement>(scene),
                    element =>
                    {
                        Assert.True(double.IsFinite(element.DesiredSize.Width));
                        Assert.True(double.IsFinite(element.DesiredSize.Height));
                        Assert.True(element.DesiredSize.Width >= 0);
                        Assert.True(element.DesiredSize.Height >= 0);
                    });
            }
        });
    }

    [Fact]
    public void Token_components_keep_shared_token_resources_and_dynamic_palette_references()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var tokenDictionary = application.Resources.MergedDictionaries.Single(dictionary =>
                dictionary.Source?.OriginalString?.EndsWith(
                    "Resources/Tokens/Metrics.xaml",
                    StringComparison.Ordinal) == true);
            var spacing = Assert.IsType<double>(application.FindResource("App.Space.24"));
            var cornerRadius = Assert.IsType<CornerRadius>(application.FindResource("App.Radius.Medium"));
            var shadow = Assert.IsType<DropShadowEffect>(application.FindResource("App.Elevation.Low"));
            var scene = GallerySceneRegistry.Build("token-components");
            var host = new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(host);
                host.UpdateLayout();

                Assert.True(spacing > 0);
                Assert.True(cornerRadius.TopLeft > 0);
                Assert.True(shadow.BlurRadius > 0);
                Assert.Contains(
                    FindDescendants<Border>(scene),
                    border => AutomationProperties.GetAutomationId(border).StartsWith(
                        "component-",
                        StringComparison.Ordinal));

                var pageHeader = FindDescendants<Border>(scene).Single(border =>
                    AutomationProperties.GetAutomationId(border) == "component-page-header");
                var lightBackground = Assert.IsType<SolidColorBrush>(pageHeader.Background);
                var lightColor = lightBackground.Color;
                Assert.Equal(spacing, Assert.IsType<double>(application.FindResource("App.Space.24")));
                Assert.Same(tokenDictionary, application.Resources.MergedDictionaries.Single(dictionary =>
                    dictionary.Source?.OriginalString?.EndsWith(
                        "Resources/Tokens/Metrics.xaml",
                        StringComparison.Ordinal) == true));

                GalleryThemeRuntime.Apply(GalleryTheme.Dark);
                host.UpdateLayout();
                Assert.Same(shadow, application.FindResource("App.Elevation.Low"));
                Assert.NotEqual(lightColor, Assert.IsType<SolidColorBrush>(pageHeader.Background).Color);
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_verified_manifest_and_stable_png_outputs()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--output",
                output.Path
            ]);
            GalleryWindow? firstWindow = null;
            GalleryWindow? secondWindow = null;
            try
            {
                var generator = new GalleryScreenshotGenerator();

                firstWindow = new GalleryWindow();
                WpfWindowHost.Show(firstWindow);
                await generator.GenerateAsync(firstWindow, options, cancellation.Token);
                var firstManifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);
                await AssertManifestMatchesPngsAsync(firstManifest, output.Path, cancellation.Token);
                var firstSnapshot = CreateSceneSnapshot(firstManifest);
                firstWindow.Close();
                firstWindow = null;

                secondWindow = new GalleryWindow();
                WpfWindowHost.Show(secondWindow);
                await generator.GenerateAsync(secondWindow, options, cancellation.Token);
                var secondManifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);
                await AssertManifestMatchesPngsAsync(secondManifest, output.Path, cancellation.Token);

                Assert.Equal(firstSnapshot, CreateSceneSnapshot(secondManifest));
            }
            finally
            {
                if (firstWindow?.IsVisible == true)
                {
                    firstWindow.Close();
                }

                if (secondWindow?.IsVisible == true)
                {
                    secondWindow.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_provider_scene_manifest()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--scene",
                "provider-style-probe",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                WpfWindowHost.Show(window);
                await new GalleryScreenshotGenerator().GenerateAsync(window, options, cancellation.Token);
                var manifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);

                await AssertManifestMatchesPngsAsync(
                    manifest,
                    output.Path,
                    ["provider-style-probe"],
                    cancellation.Token);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_palette_scene_manifest()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--scene",
                "palette-probe",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                WpfWindowHost.Show(window);
                await new GalleryScreenshotGenerator().GenerateAsync(window, options, cancellation.Token);
                var manifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);

                await AssertManifestMatchesPngsAsync(
                    manifest,
                    output.Path,
                    ["palette-probe"],
                    cancellation.Token);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_button_scene_manifest()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--scene",
                "button-styles",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                WpfWindowHost.Show(window);
                await new GalleryScreenshotGenerator().GenerateAsync(window, options, cancellation.Token);
                var manifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);

                await AssertManifestMatchesPngsAsync(
                    manifest,
                    output.Path,
                    ["button-styles"],
                    cancellation.Token);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_input_controls_scene_manifest()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--scene",
                "input-controls",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                WpfWindowHost.Show(window);
                await new GalleryScreenshotGenerator().GenerateAsync(window, options, cancellation.Token);
                var manifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);

                await AssertManifestMatchesPngsAsync(
                    manifest,
                    output.Path,
                    ["input-controls"],
                    cancellation.Token);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_list_components_scene_manifest()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--scene",
                "list-components",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                WpfWindowHost.Show(window);
                await new GalleryScreenshotGenerator().GenerateAsync(window, options, cancellation.Token);
                var manifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);

                await AssertManifestMatchesPngsAsync(
                    manifest,
                    output.Path,
                    ["list-components"],
                    cancellation.Token);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_navigation_feedback_scene_manifest()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--scene",
                "navigation-feedback",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                WpfWindowHost.Show(window);
                await new GalleryScreenshotGenerator().GenerateAsync(window, options, cancellation.Token);
                var manifest = await ReadManifestAsync(output.ManifestPath, cancellation.Token);

                await AssertManifestMatchesPngsAsync(
                    manifest,
                    output.Path,
                    ["navigation-feedback"],
                    cancellation.Token);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
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

    private static bool IsFiniteAndPositive(double value) =>
        double.IsFinite(value) && value > 0;

    private static double ContrastRatio(Color foreground, Color background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linearize(color.R)) +
               (0.7152 * Linearize(color.G)) +
               (0.0722 * Linearize(color.B));
    }

    private static async Task<GalleryManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Assert.IsType<GalleryManifest>(
            await JsonSerializer.DeserializeAsync<GalleryManifest>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken));
    }

    private static async Task AssertManifestMatchesPngsAsync(
        GalleryManifest manifest,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        await AssertManifestMatchesPngsAsync(
            manifest,
            outputDirectory,
            GallerySceneRegistry.All.Select(scene => scene.Name).ToArray(),
            cancellationToken);
    }

    private static async Task AssertManifestMatchesPngsAsync(
        GalleryManifest manifest,
        string outputDirectory,
        IReadOnlyCollection<string> expectedSceneNames,
        CancellationToken cancellationToken)
    {
        Assert.Equal(
            expectedSceneNames.Count == GallerySceneRegistry.All.Count
                ? "gallery"
                : Assert.Single(expectedSceneNames),
            manifest.ArtifactId);
        Assert.Equal("NovelSpeaker.StyleGallery", manifest.Tool);
        Assert.Equal(GalleryRenderSettings.WindowWidth, manifest.WindowWidth);
        Assert.Equal(GalleryRenderSettings.WindowHeight, manifest.WindowHeight);
        Assert.Equal(GalleryRenderSettings.Dpi, manifest.Dpi);

        var registeredScenes = GallerySceneRegistry.All.ToDictionary(scene => scene.Name, StringComparer.Ordinal);
        Assert.Equal(expectedSceneNames.Count * 2, manifest.Scenes.Count);
        Assert.Equal(
            expectedSceneNames.Order(StringComparer.Ordinal),
            manifest.Scenes.Select(scene => scene.Scene).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Dark", "Light"],
            manifest.Scenes.Select(scene => scene.Theme).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        foreach (var entry in manifest.Scenes)
        {
            Assert.True(registeredScenes.TryGetValue(entry.Scene, out var scene));
            Assert.NotNull(scene);
            Assert.True(entry.Theme is "Light" or "Dark");
            Assert.Equal(scene!.Width, entry.Width);
            Assert.Equal(scene.Height, entry.Height);
            Assert.Equal(GalleryRenderSettings.Dpi, entry.Dpi);
            Assert.False(Path.IsPathRooted(entry.Png));
            Assert.Equal(entry.Png, Path.GetFileName(entry.Png));

            var pngPath = Path.Combine(outputDirectory, entry.Png);
            var pngBytes = await File.ReadAllBytesAsync(pngPath, cancellationToken);
            Assert.NotEmpty(pngBytes);
            Assert.Equal(
                entry.Sha256,
                Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant());

            await using var stream = new MemoryStream(pngBytes, writable: false);
            var frame = Assert.Single(
                BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames);
            Assert.Equal(entry.Width, frame.PixelWidth);
            Assert.Equal(entry.Height, frame.PixelHeight);
            Assert.InRange(
                frame.DpiX,
                GalleryRenderSettings.Dpi - 0.1,
                GalleryRenderSettings.Dpi + 0.1);
            Assert.InRange(
                frame.DpiY,
                GalleryRenderSettings.Dpi - 0.1,
                GalleryRenderSettings.Dpi + 0.1);
        }
    }

    private static string[] CreateSceneSnapshot(GalleryManifest manifest) =>
        manifest.Scenes
            .Select(scene => $"{scene.Theme}|{scene.Scene}|{scene.Width}x{scene.Height}|{scene.Dpi}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private sealed class TemporaryOutputDirectory : IDisposable
    {
        public TemporaryOutputDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "NovelSpeakerStyleGalleryTests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public string ManifestPath => System.IO.Path.Combine(Path, "manifest.json");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
