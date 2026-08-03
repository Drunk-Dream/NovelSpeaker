using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using Wpf.Ui.Controls;

namespace NovelSpeaker.StyleGallery;

internal static class GallerySceneBuilders
{
    private const double SectionGap = 16;

    public static FrameworkElement CreateProviderControls() =>
        CreateSceneRoot(
            "provider-controls",
            "Provider standard controls",
            "Wpf.Ui owns the standard templates; this scene records their measurable states.",
            CreateProviderContent);

    public static FrameworkElement CreateThemeResourceProbe() =>
        CreateSceneRoot(
            "theme-resource-probe",
            "Theme resource probe",
            "DynamicResource values should change when the provider theme changes.",
            CreateThemeProbeContent);

    public static FrameworkElement CreatePlaceholderSections() =>
        CreateSceneRoot(
            "placeholder-sections",
            "Reserved component sections",
            "These areas intentionally remain placeholders until their dedicated backlog tasks.",
            CreatePlaceholderContent);

    private static Grid CreateSceneRoot(
        string automationId,
        string title,
        string description,
        Func<Panel> contentFactory)
    {
        var root = new Grid
        {
            Background = null,
            Width = GalleryRenderSettings.WindowWidth,
            Height = GalleryRenderSettings.WindowHeight,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        root.SetResourceReference(Panel.BackgroundProperty, "GalleryCanvasBackgroundBrush");
        AutomationProperties.SetAutomationId(root, automationId);

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel
        {
            Margin = new Thickness(32, 28, 32, 20)
        };
        header.Children.Add(CreateText(title, 24, FontWeights.SemiBold));
        header.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 900
        }.WithResource(TextBlock.ForegroundProperty, "GallerySecondaryTextBrush"));
        root.Children.Add(header);

        var content = contentFactory();
        content.Margin = new Thickness(32, 0, 32, 32);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        return root;
    }

    private static Panel CreateProviderContent()
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var controlsSurface = CreateSurface();
        controlsSurface.Margin = new Thickness(0, 0, SectionGap / 2, 0);
        Grid.SetColumn(controlsSurface, 0);
        var controls = new StackPanel();
        controls.Children.Add(CreateSurfaceLabel("Default and disabled controls"));
        controls.Children.Add(CreateField("TextBox", new TextBox
        {
            Text = "测试数据：Provider TextBox",
            MinHeight = 36,
            Padding = new Thickness(12, 6, 12, 6),
        }));
        controls.Children.Add(CreateField("ComboBox", new ComboBox
        {
            ItemsSource = new[] { "Light", "Dark", "System" },
            SelectedIndex = 0,
            MinHeight = 36,
        }));
        controls.Children.Add(CreateField("CheckBox", new CheckBox
        {
            Content = "保留键盘焦点语义",
            IsChecked = true,
        }));
        controls.Children.Add(CreateField("ToggleSwitch", new ToggleSwitch
        {
            Content = "启用主题探针",
            IsChecked = true,
        }));
        controls.Children.Add(CreateField("Disabled Button", new Button
        {
            Content = "Disabled action",
            IsEnabled = false,
            MinWidth = 150,
            MinHeight = 36,
        }));
        controlsSurface.Child = controls;
        content.Children.Add(controlsSurface);

        var stateSurface = CreateSurface();
        stateSurface.Margin = new Thickness(SectionGap / 2, 0, 0, 0);
        Grid.SetColumn(stateSurface, 1);
        var states = new StackPanel();
        states.Children.Add(CreateSurfaceLabel("Range, progress and icon states"));
        states.Children.Add(CreateField("Slider", new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 64,
            MinWidth = 300
        }));
        states.Children.Add(CreateField("ProgressBar", new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 64,
            Height = 8,
            MinWidth = 300
        }));
        states.Children.Add(new TextBlock
        {
            Text = "SymbolIcon and explicit Button content",
            Margin = new Thickness(0, 16, 0, 8)
        }.WithResource(TextBlock.ForegroundProperty, "GallerySecondaryTextBrush"));
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal };
        var primary = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new SymbolIcon { Symbol = SymbolRegular.PlayCircle24, Width = 20, Height = 20 },
                    new TextBlock { Text = "  Primary action", VerticalAlignment = VerticalAlignment.Center }
                }
            },
            MinWidth = 168,
            MinHeight = 40
        };
        primary.SetResourceReference(Control.BackgroundProperty, "GalleryAccentBrush");
        primary.SetResourceReference(Control.ForegroundProperty, "GalleryOnAccentTextBrush");
        AutomationProperties.SetName(primary, "Provider primary action");
        actionRow.Children.Add(primary);
        var subtle = new Button
        {
            Content = new SymbolIcon { Symbol = SymbolRegular.Settings24, Width = 20, Height = 20 },
            Width = 44,
            Height = 40,
            Margin = new Thickness(12, 0, 0, 0),
            ToolTip = "Open settings"
        };
        subtle.SetResourceReference(Control.BackgroundProperty, "GalleryMutedSurfaceBrush");
        subtle.SetResourceReference(Control.ForegroundProperty, "GalleryPrimaryTextBrush");
        AutomationProperties.SetName(subtle, "Provider settings icon action");
        actionRow.Children.Add(subtle);
        states.Children.Add(actionRow);
        states.Children.Add(new TextBlock
        {
            Text = "Long Chinese text：这是一段固定测试数据，用来确保标准控件在固定窗口和 DPI 下仍然保留可见内容。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 24, 0, 0)
        }.WithResource(TextBlock.ForegroundProperty, "GalleryPrimaryTextBrush"));
        stateSurface.Child = states;
        content.Children.Add(stateSurface);

        return content;
    }

    private static Panel CreateThemeProbeContent()
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var probeSurface = CreateSurface();
        probeSurface.Margin = new Thickness(0, 0, SectionGap / 2, 0);
        Grid.SetColumn(probeSurface, 0);
        var probes = new StackPanel();
        probes.Children.Add(CreateSurfaceLabel("Dynamic brush keys"));
        foreach (var (key, label) in new[]
                 {
                     ("SolidBackgroundFillColorBaseBrush", "Canvas background"),
                     ("CardBackgroundFillColorDefaultBrush", "Primary surface"),
                     ("LayerFillColorAltBrush", "Secondary surface"),
                     ("AccentFillColorDefaultBrush", "Accent"),
                     ("TextFillColorPrimaryBrush", "Primary text"),
                     ("TextFillColorSecondaryBrush", "Secondary text")
                 })
        {
            var swatch = new Border
            {
                Height = 42,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1)
            };
            swatch.SetResourceReference(Border.BackgroundProperty, key);
            swatch.SetResourceReference(Border.BorderBrushProperty, "GalleryBorderBrush");
            swatch.Child = new TextBlock
            {
                Text = $"{label}  ·  {key}",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }.WithResource(TextBlock.ForegroundProperty, "GalleryPrimaryTextBrush");
            probes.Children.Add(swatch);
        }
        probeSurface.Child = probes;
        content.Children.Add(probeSurface);

        var sampleSurface = CreateSurface();
        sampleSurface.Margin = new Thickness(SectionGap / 2, 0, 0, 0);
        Grid.SetColumn(sampleSurface, 1);
        var samples = new StackPanel();
        samples.Children.Add(CreateSurfaceLabel("Resource-backed text and controls"));
        samples.Children.Add(CreateText("Primary text sample", 20, FontWeights.SemiBold));
        samples.Children.Add(new TextBlock
        {
            Text = "Secondary text remains readable across both provider themes.",
            Margin = new Thickness(0, 8, 0, 18),
            TextWrapping = TextWrapping.Wrap
        }.WithResource(TextBlock.ForegroundProperty, "GallerySecondaryTextBrush"));
        samples.Children.Add(new Button
        {
            Content = "Accent resource button",
            MinWidth = 200,
            MinHeight = 40
        }.WithResource(Control.BackgroundProperty, "GalleryAccentBrush")
         .WithResource(Control.ForegroundProperty, "GalleryOnAccentTextBrush"));
        samples.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 24, 0, 24)
        }.WithResource(Border.BackgroundProperty, "GalleryBorderBrush"));
        samples.Children.Add(new TextBlock
        {
            Text = "The scene tree is identical in Light and Dark; only resource values change.",
            TextWrapping = TextWrapping.Wrap
        }.WithResource(TextBlock.ForegroundProperty, "GalleryPrimaryTextBrush"));
        sampleSurface.Child = samples;
        content.Children.Add(sampleSurface);

        return content;
    }

    private static Panel CreatePlaceholderContent()
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        AddPlaceholder(content, "Palette", "Semantic colors, surfaces and status tones", 0, 0);
        AddPlaceholder(content, "Components", "Buttons, inputs, lists and feedback", 1, 0);
        AddPlaceholder(content, "Layout contracts", "Fixed geometry, focus and accessibility probes", 0, 1);
        AddPlaceholder(content, "Future gallery scenes", "Reserved for later backlog tasks", 1, 1);
        return content;
    }

    private static void AddPlaceholder(Grid parent, string title, string description, int column, int row)
    {
        var surface = CreateSurface();
        surface.Margin = new Thickness(
            column == 0 ? 0 : SectionGap / 2,
            row == 0 ? 0 : SectionGap / 2,
            column == 1 ? 0 : SectionGap / 2,
            row == 1 ? 0 : SectionGap / 2);
        surface.Child = new StackPanel
        {
            Children =
            {
                CreateSurfaceLabel(title),
                new TextBlock
                {
                    Text = description,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                }.WithResource(TextBlock.ForegroundProperty, "GallerySecondaryTextBrush"),
                new TextBlock
                {
                    Text = "Placeholder · task 03",
                    Margin = new Thickness(0, 24, 0, 0)
                }.WithResource(TextBlock.ForegroundProperty, "GalleryTertiaryTextBrush")
            }
        };
        Grid.SetColumn(surface, column);
        Grid.SetRow(surface, row);
        parent.Children.Add(surface);
    }

    private static Border CreateSurface()
    {
        var surface = new Border
        {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        surface.SetResourceReference(Border.BackgroundProperty, "GallerySurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "GalleryBorderBrush");
        return surface;
    }

    private static TextBlock CreateSurfaceLabel(string text) =>
        CreateText(text, 16, FontWeights.SemiBold);

    private static TextBlock CreateText(string text, double fontSize, FontWeight fontWeight) =>
        new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight
        }.WithResource(TextBlock.ForegroundProperty, "GalleryPrimaryTextBrush");

    private static Border CreateField(string label, Control control)
    {
        AutomationProperties.SetName(control, $"Provider {label}");
        control.SetResourceReference(Control.ForegroundProperty, "GalleryPrimaryTextBrush");
        var field = new Border { Margin = new Thickness(0, 16, 0, 0) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 6)
        }.WithResource(TextBlock.ForegroundProperty, "GallerySecondaryTextBrush"));
        stack.Children.Add(control);
        field.Child = stack;
        return field;
    }

    private static T WithResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
