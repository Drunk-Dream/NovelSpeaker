using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using PasswordBox = System.Windows.Controls.PasswordBox;
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

    public static FrameworkElement CreatePaletteProbe() =>
        CreateSceneRoot(
            "palette-probe",
            "Semantic palette",
            "Every stable palette brush is shown with readable text and icon samples; only values change between themes.",
            CreatePaletteContent);

    public static FrameworkElement CreateProviderStyleProbe() =>
        CreateSceneRoot(
            "provider-style-probe",
            "Provider Style Bridge probe",
            "Each explicit alias keeps the Wpf.Ui template and exposes its measurable interaction contract.",
            CreateProviderStyleProbeContent);

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

    private static Panel CreatePaletteContent()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var container = new Grid();
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, SectionGap / 2, 0) };
        var right = new StackPanel { Margin = new Thickness(SectionGap / 2, 0, 0, 0) };
        var swatches = new[]
        {
            ("AppBackgroundBrush", "PrimaryTextBrush", "App background"),
            ("CanvasSurfaceBrush", "PrimaryTextBrush", "Canvas surface"),
            ("PrimarySurfaceBrush", "PrimaryTextBrush", "Primary surface"),
            ("SecondarySurfaceBrush", "PrimaryTextBrush", "Secondary surface"),
            ("RaisedSurfaceBrush", "PrimaryTextBrush", "Raised surface"),
            ("PrimaryTextBrush", "PrimarySurfaceBrush", "Primary text"),
            ("SecondaryTextBrush", "PrimarySurfaceBrush", "Secondary text"),
            ("TertiaryTextBrush", "PrimarySurfaceBrush", "Tertiary text"),
            ("SubtleBorderBrush", "PrimaryTextBrush", "Subtle border"),
            ("StrongBorderBrush", "PrimaryTextBrush", "Strong border"),
            ("AccentBrush", "AccentTextBrush", "Accent"),
            ("AccentDefaultBrush", "AccentTextBrush", "Accent default"),
            ("AccentHoverBrush", "AccentTextBrush", "Accent hover"),
            ("AccentPressedBrush", "AccentTextBrush", "Accent pressed"),
            ("AccentSubtleBrush", "PrimaryTextBrush", "Accent subtle"),
            ("AccentFocusRingBrush", "AccentTextBrush", "Accent focus ring"),
            ("AccentTextBrush", "AccentBrush", "Accent text"),
            ("DangerBrush", "DangerTextBrush", "Danger"),
            ("DangerSubtleBrush", "PrimaryTextBrush", "Danger subtle"),
            ("DangerTextBrush", "DangerBrush", "Danger text"),
            ("WarningBrush", "WarningTextBrush", "Warning"),
            ("WarningSubtleBrush", "PrimaryTextBrush", "Warning subtle"),
            ("WarningTextBrush", "WarningBrush", "Warning text"),
            ("SuccessBrush", "SuccessTextBrush", "Success"),
            ("SuccessSubtleBrush", "PrimaryTextBrush", "Success subtle"),
            ("SuccessTextBrush", "SuccessBrush", "Success text")
        };

        for (var index = 0; index < swatches.Length; index++)
        {
            var swatch = CreatePaletteSwatch(swatches[index].Item1, swatches[index].Item2, swatches[index].Item3);
            (index < (swatches.Length + 1) / 2 ? left : right).Children.Add(swatch);
        }

        columns.Children.Add(left);
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);
        scrollViewer.Content = columns;
        container.Children.Add(scrollViewer);
        return container;
    }

    private static Border CreatePaletteSwatch(string backgroundKey, string foregroundKey, string label)
    {
        var icon = new SymbolIcon
        {
            Symbol = SymbolRegular.Circle24,
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, foregroundKey);

        var text = new TextBlock
        {
            Text = $"{label}  ·  {backgroundKey}",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(text);

        var swatch = new Border
        {
            MinHeight = 42,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = row
        };
        swatch.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        swatch.SetResourceReference(Border.BorderBrushProperty, "SubtleBorderBrush");
        AutomationProperties.SetAutomationId(swatch, $"palette-{backgroundKey}");
        return swatch;
    }

    private static Panel CreateProviderStyleProbeContent()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(20, 16, 20, 10) };
        foreach (var width in new[] { 170d, 190d, 190d, 86d, 86d, 180d, 140d })
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        foreach (var (label, column) in new[]
                 {
                     ("Bridge alias", 0),
                     ("Default", 1),
                     ("Disabled", 2),
                     ("Template", 3),
                     ("Min size", 4),
                     ("Content alignment", 5),
                     ("Focus", 6)
                 })
        {
            var text = CreateText(label, 12, FontWeights.SemiBold);
            Grid.SetColumn(text, column);
            header.Children.Add(text);
        }

        Grid.SetRow(header, 0);
        content.Children.Add(header);

        var rows = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
        foreach (var key in ProviderStyleBridgeKeys)
        {
            rows.Children.Add(CreateProviderStyleProbeRow(key));
        }

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rows
        };
        Grid.SetRow(scrollViewer, 1);
        content.Children.Add(scrollViewer);
        return content;
    }

    private static Border CreateProviderStyleProbeRow(string resourceKey)
    {
        var defaultControl = CreateProviderControl(resourceKey);
        var disabledControl = CreateProviderControl(resourceKey);
        disabledControl.IsEnabled = false;

        var defaultVisual = CreateProviderProbeVisual(defaultControl);
        var disabledVisual = CreateProviderProbeVisual(disabledControl);
        ApplyTemplate(defaultVisual, defaultControl);
        ApplyTemplate(disabledVisual, disabledControl);

        var row = new Grid { MinHeight = 56, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var width in new[] { 170d, 190d, 190d, 86d, 86d, 180d, 140d })
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        var keyText = CreateText(resourceKey, 12, FontWeights.SemiBold);
        keyText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(keyText, 0);
        row.Children.Add(keyText);

        defaultVisual.VerticalAlignment = VerticalAlignment.Center;
        defaultVisual.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(defaultVisual, 1);
        row.Children.Add(defaultVisual);

        disabledVisual.VerticalAlignment = VerticalAlignment.Center;
        disabledVisual.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(disabledVisual, 2);
        row.Children.Add(disabledVisual);

        AddProbeText(row, defaultControl.Template is not null ? "non-empty" : "missing", 3);
        AddProbeText(
            row,
            $"{FormatDimension(defaultControl.MinWidth)} × {FormatDimension(defaultControl.MinHeight)}",
            4);
        AddProbeText(
            row,
            $"{defaultControl.HorizontalContentAlignment}\n{defaultControl.VerticalContentAlignment}",
            5);
        AddProbeText(
            row,
            defaultControl.Focusable ? "Focusable\nprovider state" : "not focusable",
            6);

        var surface = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1),
            Child = row
        };
        surface.SetResourceReference(Border.BackgroundProperty, "GalleryMutedSurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "GalleryBorderBrush");
        return surface;
    }

    private static void AddProbeText(Grid row, string value, int column)
    {
        var text = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        }.WithResource(TextBlock.ForegroundProperty, "GallerySecondaryTextBrush");
        Grid.SetColumn(text, column);
        row.Children.Add(text);
    }

    private static Control CreateProviderControl(string resourceKey)
    {
        Control control = resourceKey switch
        {
            "Provider.Button" => new Button { Content = "Provider button", MinHeight = 32 },
            "Provider.TextBox" => new TextBox { Text = "Provider text", MinHeight = 32 },
            "Provider.PasswordBox" => new PasswordBox { Password = "fixture", MinHeight = 32 },
            "Provider.ComboBox" => new ComboBox
            {
                ItemsSource = new[] { "Light", "Dark" },
                SelectedIndex = 0,
                MinHeight = 32
            },
            "Provider.CheckBox" => new CheckBox { Content = "Provider check", IsChecked = true },
            "Provider.ToggleSwitch" => new ToggleSwitch { Content = "Provider toggle", IsChecked = true },
            "Provider.NavigationViewItem" => new NavigationViewItem { Content = "Provider navigation" },
            "Provider.Slider" => new Slider { Minimum = 0, Maximum = 100, Value = 50, MinWidth = 120 },
            _ => throw new InvalidOperationException($"Unknown provider bridge key '{resourceKey}'.")
        };

        control.Style = Application.Current?.FindResource(resourceKey) as Style
            ?? throw new InvalidOperationException($"Provider bridge resource '{resourceKey}' was not found.");
        AutomationProperties.SetName(control, resourceKey);
        control.SetResourceReference(Control.ForegroundProperty, "GalleryPrimaryTextBrush");
        return control;
    }

    private static FrameworkElement CreateProviderProbeVisual(Control control)
    {
        if (control is not NavigationViewItem navigationItem)
        {
            return control;
        }

        var navigation = new NavigationView
        {
            Width = 180,
            Height = 48,
            IsPaneOpen = true,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left
        };
        navigation.MenuItems.Add(navigationItem);
        return navigation;
    }

    private static void ApplyTemplate(FrameworkElement visual, Control control)
    {
        visual.Measure(new Size(180, 48));
        visual.Arrange(new Rect(0, 0, 180, 48));
        visual.UpdateLayout();
        control.ApplyTemplate();
    }

    private static string FormatDimension(double value) =>
        double.IsNaN(value) ? "auto" : value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string[] ProviderStyleBridgeKeys =
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
