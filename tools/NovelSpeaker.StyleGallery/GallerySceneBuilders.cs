using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
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

    public static FrameworkElement CreateTokenComponents() =>
        CreateSceneRoot(
            "token-components",
            "Stable token components",
            "PageHeader, SectionSurface and StatusView use the shared token contract and dynamic semantic palette.",
            CreateTokenComponentsContent);

    public static FrameworkElement CreateButtonStyles() =>
        CreateSceneRoot(
            "button-styles",
            "Named button styles",
            "App.Button variants inherit the Wpf.Ui provider template; only explicit semantic values and states are owned here.",
            CreateButtonStylesContent);

    public static FrameworkElement CreateMediaControls() =>
        CreateSceneRoot(
            "media-controls",
            "Media control components",
            "App.Button.Icon and the Gallery-only control bar show playback, volume, window actions and deterministic slider projection.",
            CreateMediaControlsContent);

    public static FrameworkElement CreateInputControls() =>
        CreateSceneRoot(
            "input-controls",
            "Input and selection controls",
            "Explicit App.Input variants cover content, density, selection, disabled, read-only and visible validation states without replacing Provider templates.",
            CreateInputControlsContent);

    public static FrameworkElement CreateListComponents() =>
        CreateSceneRoot(
            "list-components",
            "List, card and settings components",
            "Shared BookCard, ListRow, SelectableRow, SettingsRow, RuleListItem and EmptyState components keep state ownership independent from virtualized containers.",
            GalleryListComponentScene.Create);

    public static FrameworkElement CreateNavigationFeedback() =>
        CreateSceneRoot(
            "navigation-feedback",
            "Navigation, menus and feedback components",
            "Shared explicit styles keep provider templates intact while making focus, dismissal, progress and request states measurable.",
            GalleryNavigationFeedbackScene.Create);

    public static FrameworkElement CreateProviderStyleProbe() =>
        CreateSceneRoot(
            "provider-style-probe",
            "Provider Style Bridge probe",
            "Each explicit alias keeps the Wpf.Ui template and exposes its measurable interaction contract.",
            CreateProviderStyleProbeContent);

    private static Grid CreateSceneRoot(
        string automationId,
        string title,
        string description,
        Func<FrameworkElement> contentFactory)
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
            ("App.Brush.Window.Background", "App.Brush.Text.Primary", "App background"),
            ("App.Brush.Canvas", "App.Brush.Text.Primary", "Canvas surface"),
            ("App.Brush.Surface.Primary", "App.Brush.Text.Primary", "Primary surface"),
            ("App.Brush.Surface.Secondary", "App.Brush.Text.Primary", "Secondary surface"),
            ("App.Brush.Surface.Raised", "App.Brush.Text.Primary", "Raised surface"),
            ("App.Brush.Text.Primary", "App.Brush.Surface.Primary", "Primary text"),
            ("App.Brush.Text.Secondary", "App.Brush.Surface.Primary", "Secondary text"),
            ("App.Brush.Text.Tertiary", "App.Brush.Surface.Primary", "Tertiary text"),
            ("App.Brush.Border.Subtle", "App.Brush.Text.Primary", "Subtle border"),
            ("App.Brush.Border.Strong", "App.Brush.Text.Primary", "Strong border"),
            ("App.Brush.Accent", "App.Brush.Accent.Text", "Accent"),
            ("App.Brush.Accent.Default", "App.Brush.Accent.Text", "Accent default"),
            ("App.Brush.Accent.Hover", "App.Brush.Accent.Text", "Accent hover"),
            ("App.Brush.Accent.Pressed", "App.Brush.Accent.Text", "Accent pressed"),
            ("App.Brush.Accent.Subtle", "App.Brush.Text.Primary", "Accent subtle"),
            ("App.Brush.Focus", "App.Brush.Accent.Text", "Accent focus ring"),
            ("App.Brush.Accent.Text", "App.Brush.Accent", "Accent text"),
            ("App.Brush.Danger", "App.Brush.Danger.Text", "Danger"),
            ("App.Brush.Danger.Subtle", "App.Brush.Text.Primary", "Danger subtle"),
            ("App.Brush.Danger.Text", "App.Brush.Danger", "Danger text"),
            ("App.Brush.Danger.Pressed", "App.Brush.Danger.Pressed.Text", "Danger pressed"),
            ("App.Brush.Danger.Pressed.Text", "App.Brush.Danger.Pressed", "Danger pressed text"),
            ("App.Brush.Warning", "App.Brush.Warning.Text", "Warning"),
            ("App.Brush.Warning.Subtle", "App.Brush.Text.Primary", "Warning subtle"),
            ("App.Brush.Warning.Text", "App.Brush.Warning", "Warning text"),
            ("App.Brush.Success", "App.Brush.Success.Text", "Success"),
            ("App.Brush.Success.Subtle", "App.Brush.Text.Primary", "Success subtle"),
            ("App.Brush.Success.Text", "App.Brush.Success", "Success text")
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

    private static FrameworkElement CreateTokenComponentsContent()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new StackPanel();
        content.Children.Add(CreatePageHeaderSample());
        content.Children.Add(CreateSectionSurfaceSample());
        content.Children.Add(CreateStatusViewSample());
        scrollViewer.Content = content;
        return scrollViewer;
    }

    private static FrameworkElement CreateButtonStylesContent()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new StackPanel();
        content.Children.Add(CreateButtonStateTable());
        content.Children.Add(CreateButtonContentSamples());
        scrollViewer.Content = content;
        return scrollViewer;
    }

    private static FrameworkElement CreateMediaControlsContent()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new StackPanel();
        content.Children.Add(new GalleryMediaControlBar
        {
            Margin = new Thickness(0, 0, 0, 16)
        });

        var stateSurface = CreateComponentSurface("media-state-fixtures");
        stateSurface.Padding = new Thickness(20);
        stateSurface.Child = new StackPanel
        {
            Children =
            {
                CreateText("State fixtures", 15, FontWeights.SemiBold),
                new TextBlock
                {
                    Text = "播放 / 暂停使用统一 48 px 外部布局与 28 px icon 容器的唯一 Accent 主按钮；上一章 / 下一章使用 32 px 低权重图标，上一段 / 下一段和音量使用 36 px 中性按钮。Focus、Disabled、置顶激活和长 Tooltip 均为固定 Gallery 状态。",
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                }.WithResource(TextBlock.ForegroundProperty, "App.Brush.Text.Secondary"),
                new TextBlock
                {
                    Text = "段落进度使用 Accent 已播放轨道和中性未播放轨道；拖动 Slider 实时更新轨道与 x / y projection fixture，不连接任何真实播放命令或音量命令。",
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                }.WithResource(TextBlock.ForegroundProperty, "App.Brush.Text.Tertiary")
            }
        };
        content.Children.Add(stateSurface);
        scrollViewer.Content = content;
        return scrollViewer;
    }

    private static FrameworkElement CreateInputControlsContent()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        AutomationProperties.SetAutomationId(scrollViewer, "input-controls-scroll-viewer");
        AutomationProperties.SetName(scrollViewer, "Input and selection controls scroll area");

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var textEntrySurface = CreateSurface();
        textEntrySurface.Margin = new Thickness(0, 0, SectionGap / 2, 0);
        Grid.SetColumn(textEntrySurface, 0);
        var textEntries = new StackPanel();
        textEntries.Children.Add(CreateSurfaceLabel("Text entry and password"));
        textEntries.Children.Add(CreateInputField(
            "Empty content · TextBox.Standard",
            CreateInputControl(
                new TextBox(),
                "App.Input.TextBox.Standard",
                "input-textbox-empty-standard",
                "TextBox empty content")));
        textEntries.Children.Add(CreateInputField(
            "Long Chinese content · TextBox.Standard",
            CreateInputControl(
                new TextBox
                {
                    Text = "长中文内容：这是用于 Style Gallery 的固定输入样本，检查文本在标准高度、固定窗口和浅色或深色主题下都保持可读，并且不会因为控件状态改变而被裁掉。"
                },
                "App.Input.TextBox.Standard",
                "input-textbox-long-standard",
                "TextBox long Chinese content")));
        textEntries.Children.Add(CreateInputField(
            "Read-only content · TextBox.Compact",
            CreateInputControl(
                new TextBox
                {
                    Text = "只读：当前章节标题不可编辑。",
                    IsReadOnly = true
                },
                "App.Input.TextBox.Compact",
                "input-textbox-readonly-compact",
                "TextBox read-only content")));
        textEntries.Children.Add(CreateInputField(
            "Disabled content · TextBox.Compact",
            CreateInputControl(
                new TextBox
                {
                    Text = "禁用：导入任务尚未完成。",
                    IsEnabled = false
                },
                "App.Input.TextBox.Compact",
                "input-textbox-disabled-compact",
                "TextBox disabled content")));

        var invalidTextBox = CreateInputControl(
            new TextBox { Text = "不完整的章节名称" },
            "App.Input.TextBox.Standard",
            "input-textbox-error-standard",
            "TextBox invalid chapter name");
        MarkInputValidationError(
            invalidTextBox,
            TextBox.TextProperty,
            "章节名称还需要包含作者信息。请补充后再继续。");
        textEntries.Children.Add(CreateInputField(
            "Error · TextBox.Standard",
            invalidTextBox,
            "章节名称还需要包含作者信息。请补充后再继续。"));

        textEntries.Children.Add(CreateInputField(
            "Password · PasswordBox.Standard",
            CreateInputControl(
                new PasswordBox { Password = "gallery-secret" },
                "App.Input.PasswordBox.Standard",
                "input-password-standard",
                "PasswordBox standard password")));
        textEntries.Children.Add(CreateInputField(
            "Disabled password · PasswordBox.Compact",
            CreateInputControl(
                new PasswordBox
                {
                    Password = "disabled-secret",
                    IsEnabled = false
                },
                "App.Input.PasswordBox.Compact",
                "input-password-disabled-compact",
                "PasswordBox disabled password")));
        textEntrySurface.Child = textEntries;
        columns.Children.Add(textEntrySurface);

        var selectionSurface = CreateSurface();
        selectionSurface.Margin = new Thickness(SectionGap / 2, 0, 0, 0);
        Grid.SetColumn(selectionSurface, 1);
        var selections = new StackPanel();
        selections.Children.Add(CreateSurfaceLabel("ComboBox, CheckBox and ToggleSwitch"));

        var comboItems = new[]
        {
            "普通章节",
            "长中文选项：这是用于检查 ComboBoxItem、Popup 宽度和换行边界的固定长项。",
            "有声书章节与正文同步",
            "未选择来源"
        };
        selections.Children.Add(CreateInputField(
            "Dropdown items · ComboBox.Standard",
            CreateInputControl(
                new ComboBox
                {
                    ItemsSource = comboItems,
                    SelectedIndex = 0,
                    MaxDropDownHeight = 180
                },
                "App.Input.ComboBox.Standard",
                "input-combobox-options-standard",
                "ComboBox dropdown options")));
        selections.Children.Add(CreateInputField(
            "Long selected item · ComboBox.Compact",
            CreateInputControl(
                new ComboBox
                {
                    ItemsSource = comboItems,
                    SelectedIndex = 1,
                    MaxDropDownHeight = 180
                },
                "App.Input.ComboBox.Compact",
                "input-combobox-long-compact",
                "ComboBox long selected item")));

        selections.Children.Add(CreateInputField(
            "Checked · CheckBox.Standard",
            CreateInputControl(
                new CheckBox
                {
                    Content = "朗读章节标题",
                    IsChecked = true
                },
                "App.Input.CheckBox.Standard",
                "input-checkbox-checked-standard",
                "CheckBox checked read chapter title")));
        selections.Children.Add(CreateInputField(
            "Unchecked · CheckBox.Standard",
            CreateInputControl(
                new CheckBox
                {
                    Content = "朗读章节脚注",
                    IsChecked = false
                },
                "App.Input.CheckBox.Standard",
                "input-checkbox-unchecked-standard",
                "CheckBox unchecked read footnotes")));
        selections.Children.Add(CreateInputField(
            "Disabled · CheckBox.Compact",
            CreateInputControl(
                new CheckBox
                {
                    Content = "禁用选项（没有可用的 TTS 声音）",
                    IsChecked = true,
                    IsEnabled = false
                },
                "App.Input.CheckBox.Compact",
                "input-checkbox-disabled-compact",
                "CheckBox disabled option")));

        var invalidCheckBox = CreateInputControl(
            new CheckBox
            {
                Content = "将章节标题合并到正文",
                IsChecked = false
            },
            "App.Input.CheckBox.Compact",
            "input-checkbox-error-compact",
            "CheckBox invalid chapter title option");
        MarkInputValidationError(
            invalidCheckBox,
            ToggleButton.IsCheckedProperty,
            "请选择是否合并章节标题，否则无法保存当前朗读配置。");
        selections.Children.Add(CreateInputField(
            "Error · CheckBox.Compact",
            invalidCheckBox,
            "请选择是否合并章节标题，否则无法保存当前朗读配置。"));

        selections.Children.Add(CreateInputField(
            "On with label · ToggleSwitch.Standard",
            CreateInputControl(
                new ToggleSwitch
                {
                    Content = "启用自动缓存",
                    IsChecked = true
                },
                "App.Input.ToggleSwitch.Standard",
                "input-toggle-labeled-on-standard",
                "ToggleSwitch on with label")));
        selections.Children.Add(CreateInputField(
            "Off with label · ToggleSwitch.Standard",
            CreateInputControl(
                new ToggleSwitch
                {
                    Content = "允许移动网络下载",
                    IsChecked = false
                },
                "App.Input.ToggleSwitch.Standard",
                "input-toggle-labeled-off-standard",
                "ToggleSwitch off with label")));
        selections.Children.Add(CreateInputField(
            "On without label · ToggleSwitch.Compact",
            CreateInputControl(
                new ToggleSwitch { IsChecked = true },
                "App.Input.ToggleSwitch.Compact",
                "input-toggle-unlabeled-on-compact",
                "ToggleSwitch on without label")));
        selections.Children.Add(CreateInputField(
            "Off and disabled · ToggleSwitch.Compact",
            CreateInputControl(
                new ToggleSwitch
                {
                    IsChecked = false,
                    IsEnabled = false
                },
                "App.Input.ToggleSwitch.Compact",
                "input-toggle-unlabeled-off-disabled-compact",
                "ToggleSwitch off disabled without label")));

        var invalidToggle = CreateInputControl(
            new ToggleSwitch
            {
                Content = "允许后台朗读",
                IsChecked = false
            },
            "App.Input.ToggleSwitch.Standard",
            "input-toggle-error-standard",
            "ToggleSwitch invalid background reading option");
        MarkInputValidationError(
            invalidToggle,
            ToggleButton.IsCheckedProperty,
            "后台朗读需要先启用本地缓存，请检查相关设置。");
        selections.Children.Add(CreateInputField(
            "Error · ToggleSwitch.Standard",
            invalidToggle,
            "后台朗读需要先启用本地缓存，请检查相关设置。"));
        selectionSurface.Child = selections;
        columns.Children.Add(selectionSurface);

        scrollViewer.Content = columns;
        return scrollViewer;
    }

    private static Border CreateButtonStateTable()
    {
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        foreach (var _ in ButtonPreviewStates)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        }

        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in ButtonStyleVariants)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddButtonTableHeader(table, "Variant", 0);
        for (var stateIndex = 0; stateIndex < ButtonPreviewStates.Length; stateIndex++)
        {
            AddButtonTableHeader(table, ButtonPreviewStates[stateIndex], stateIndex + 1);
        }

        for (var variantIndex = 0; variantIndex < ButtonStyleVariants.Length; variantIndex++)
        {
            var variant = ButtonStyleVariants[variantIndex];
            var label = CreateText(variant, 13, FontWeights.SemiBold);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 8, 12, 8);
            Grid.SetRow(label, variantIndex + 1);
            Grid.SetColumn(label, 0);
            table.Children.Add(label);

            for (var stateIndex = 0; stateIndex < ButtonPreviewStates.Length; stateIndex++)
            {
                var state = ButtonPreviewStates[stateIndex];
                var button = CreateButtonPreview(variant, state);
                Grid.SetRow(button, variantIndex + 1);
                Grid.SetColumn(button, stateIndex + 1);
                table.Children.Add(button);
            }
        }

        var surface = CreateComponentSurface("button-style-state-table");
        surface.Padding = new Thickness(20);
        surface.Child = table;
        return surface;
    }

    private static void AddButtonTableHeader(Grid table, string text, int column)
    {
        var header = CreateText(text, 12, FontWeights.SemiBold);
        header.Margin = new Thickness(0, 0, 12, 8);
        Grid.SetColumn(header, column);
        table.Children.Add(header);
    }

    private static Button CreateButtonPreview(string variant, string state)
    {
        var button = new Button
        {
            Style = FindButtonStyle(variant),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 12, 8),
            Content = variant == "Icon"
                ? CreateButtonIcon(SymbolRegular.Settings24, "App.Brush.Text.Primary")
                : variant,
            ToolTip = $"{variant} · {state}"
        };
        AutomationProperties.SetAutomationId(
            button,
            $"button-{variant.ToLowerInvariant()}-{state.ToLowerInvariant()}");
        AutomationProperties.SetName(button, $"App.Button.{variant} {state}");
        ApplyButtonPreviewState(button, variant, state);
        return button;
    }

    private static void ApplyButtonPreviewState(Button button, string variant, string state)
    {
        if (state == "Disabled")
        {
            button.IsEnabled = false;
            return;
        }

        var backgroundKey = state switch
        {
            "Hover" => variant is "Primary" ? "App.Brush.Accent.Hover" :
                       variant is "Danger" ? "App.Brush.Danger.Subtle" :
                       variant is "Secondary" ? "App.Brush.Surface.Secondary" : "App.Brush.Accent.Subtle",
            "Pressed" => variant is "Primary" ? "App.Brush.Accent.Pressed" :
                         variant is "Secondary" ? "App.Brush.Accent.Subtle" :
                         variant is "Danger" ? "App.Brush.Danger.Pressed" : "App.Brush.Surface.Secondary",
            _ => null
        };
        if (backgroundKey is not null)
        {
            button.SetResourceReference(Control.BackgroundProperty, backgroundKey);
        }

        if (state == "Hover" && variant == "Danger")
        {
            button.SetResourceReference(Control.ForegroundProperty, "App.Brush.Text.Primary");
        }

        if (state == "Pressed" && variant == "Danger")
        {
            button.SetResourceReference(Control.ForegroundProperty, "App.Brush.Danger.Pressed.Text");
            button.SetResourceReference(Control.BorderBrushProperty, "App.Brush.Danger.Pressed");
        }

        if (state == "Focus")
        {
            button.SetResourceReference(Control.BorderBrushProperty, "App.Brush.Focus");
        }
    }

    private static Border CreateButtonContentSamples()
    {
        var grid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconAndTextSymbol = CreateButtonIcon(SymbolRegular.PlayCircle24, "App.Brush.Accent.Text");
        iconAndTextSymbol.Width = 20;
        iconAndTextSymbol.Height = 20;
        iconAndTextSymbol.Margin = new Thickness(0, 0, 8, 0);
        var iconAndText = new Button
        {
            Style = FindButtonStyle("Primary"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    iconAndTextSymbol,
                    new TextBlock
                    {
                        Text = "图标 + 文本",
                        VerticalAlignment = VerticalAlignment.Center
                    }.WithResource(TextBlock.ForegroundProperty, "App.Brush.Accent.Text")
                }
            }
        };
        AutomationProperties.SetAutomationId(
            iconAndTextSymbol,
            "button-icon-text-symbol");
        AutomationProperties.SetAutomationId(iconAndText, "button-icon-text");
        AutomationProperties.SetName(iconAndText, "App.Button.Primary icon and text");
        Grid.SetColumn(iconAndText, 0);
        grid.Children.Add(iconAndText);

        var longText = new Button
        {
            Style = FindButtonStyle("Secondary"),
            Width = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new TextBlock
            {
                Text = "长中文文本：这是一个固定的按钮内容 fixture，用来验证具名样式在宽窗口与不同 DPI 下保持完整可见，不通过裁剪或改变外部布局来隐藏文字。",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480
            }
        };
        AutomationProperties.SetAutomationId(longText, "button-long-text");
        AutomationProperties.SetName(longText, "App.Button.Secondary long Chinese text");
        Grid.SetColumn(longText, 1);
        grid.Children.Add(longText);

        var surface = CreateComponentSurface("button-style-content-samples");
        surface.Padding = new Thickness(20);
        surface.Child = new StackPanel
        {
            Children =
            {
                CreateText("Content variations", 15, FontWeights.SemiBold),
                grid
            }
        };
        return surface;
    }

    private static SymbolIcon CreateButtonIcon(SymbolRegular symbol, string foregroundKey)
    {
        var icon = new SymbolIcon { Symbol = symbol };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, foregroundKey);
        icon.SetResourceReference(TextElement.ForegroundProperty, foregroundKey);
        icon.Loaded += (_, _) => ApplyButtonIconGlyphForeground(icon, foregroundKey);
        return icon;
    }

    private static void ApplyButtonIconGlyphForeground(SymbolIcon icon, string foregroundKey)
    {
        icon.ApplyTemplate();
        foreach (var glyph in FindVisualDescendants<TextBlock>(icon))
        {
            glyph.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        }
    }

    private static IReadOnlyList<T> FindVisualDescendants<T>(DependencyObject root)
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

    private static Style FindButtonStyle(string variant) =>
        System.Windows.Application.Current?.FindResource($"App.Button.{variant}") as Style
        ?? throw new InvalidOperationException($"Button style 'App.Button.{variant}' was not found.");

    private static readonly string[] ButtonStyleVariants =
    [
        "Primary",
        "Secondary",
        "Subtle",
        "Icon",
        "Danger"
    ];

    private static readonly string[] ButtonPreviewStates =
    [
        "Default",
        "Hover",
        "Pressed",
        "Focus",
        "Disabled"
    ];

    private static Border CreatePageHeaderSample()
    {
        var title = CreateTokenText(
            "PageHeader · 长标题在窄空间中保持可读并自然换行",
            "App.Text.Size.PageTitle",
            "App.Text.Weight.SemiBold",
            "App.Brush.Text.Primary");
        AutomationProperties.SetAutomationId(title, "component-page-header-title");

        var description = CreateTokenText(
            "这是一个固定 fixture，用来验证标题、说明文字和操作入口在 Light/Dark 以及不同 DPI 下保持清晰。",
            "App.Text.Size.Secondary",
            "App.Text.Weight.Regular",
            "App.Brush.Text.Secondary",
            "App.Text.LineHeight.Secondary");
        AutomationProperties.SetAutomationId(description, "component-page-header-description");

        var copy = new StackPanel();
        copy.Children.Add(title);
        description.Margin = new Thickness(0, TokenDouble("App.Space.8"), 0, 0);
        copy.Children.Add(description);

        var icon = new SymbolIcon
        {
            Symbol = SymbolRegular.DocumentText24,
            VerticalAlignment = VerticalAlignment.Top
        };
        icon.SetResourceReference(FrameworkElement.WidthProperty, "App.Size.Icon.Large");
        icon.SetResourceReference(FrameworkElement.HeightProperty, "App.Size.Icon.Large");
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "App.Brush.Accent");
        AutomationProperties.SetAutomationId(icon, "component-page-header-icon");

        var action = new Button
        {
            Content = "示例动作",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = TokenPadding("App.Space.12")
        };
        action.Style = System.Windows.Application.Current?.FindResource("Provider.Button") as Style;
        action.SetResourceReference(FrameworkElement.MinHeightProperty, "App.Size.Control.Compact");
        action.SetResourceReference(Control.BackgroundProperty, "App.Brush.Accent");
        action.SetResourceReference(Control.ForegroundProperty, "App.Brush.Accent.Text");
        AutomationProperties.SetName(action, "PageHeader sample action");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        icon.Margin = new Thickness(0, 0, TokenDouble("App.Space.12"), 0);
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(copy, 1);
        Grid.SetColumn(action, 2);
        grid.Children.Add(icon);
        grid.Children.Add(copy);
        grid.Children.Add(action);

        var surface = CreateComponentSurface("component-page-header");
        surface.Padding = TokenPadding("App.Space.24");
        surface.Child = grid;
        return surface;
    }

    private static Border CreateSectionSurfaceSample()
    {
        var title = CreateTokenText(
            "SectionSurface · 主要内容",
            "App.Text.Size.SectionTitle",
            "App.Text.Weight.SemiBold",
            "App.Brush.Text.Primary");
        AutomationProperties.SetAutomationId(title, "component-section-surface-title");

        var body = CreateTokenText(
            "表面组件通过层级、留白和轻微抬升建立分组，不引入页面列宽、规则列表宽度或补偿性 Padding。长文本 fixture 也应保持可见。",
            "App.Text.Size.Body",
            "App.Text.Weight.Regular",
            "App.Brush.Text.Primary",
            "App.Text.LineHeight.Body");
        AutomationProperties.SetAutomationId(body, "component-section-surface-body");
        body.Margin = new Thickness(0, TokenDouble("App.Space.12"), 0, 0);

        var hint = new Border
        {
            Padding = TokenPadding("App.Space.8"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = CreateTokenText(
                "App.Elevation.Low · PrimarySurface",
                "App.Text.Size.Caption",
                "App.Text.Weight.Regular",
                "App.Brush.Text.Secondary")
        };
        hint.SetResourceReference(Border.BackgroundProperty, "App.Brush.Surface.Secondary");
        hint.SetResourceReference(Border.BorderBrushProperty, "App.Brush.Border.Subtle");
        hint.SetResourceReference(Border.CornerRadiusProperty, "App.Radius.Small");
        hint.Margin = new Thickness(0, TokenDouble("App.Space.16"), 0, 0);
        AutomationProperties.SetAutomationId(hint, "component-section-surface-hint");

        var content = new StackPanel();
        content.Children.Add(title);
        content.Children.Add(body);
        content.Children.Add(hint);

        var surface = CreateComponentSurface("component-section-surface");
        surface.Padding = TokenPadding("App.Space.20");
        surface.SetResourceReference(UIElement.EffectProperty, "App.Elevation.Low");
        surface.Child = content;
        return surface;
    }

    private static Border CreateStatusViewSample()
    {
        var content = new StackPanel();
        content.Children.Add(CreateTokenText(
            "StatusView · 可读状态",
            "App.Text.Size.SectionTitle",
            "App.Text.Weight.SemiBold",
            "App.Brush.Text.Primary"));
        content.Children.Add(CreateStatusRow(
            "component-status-view-success",
            SymbolRegular.CheckmarkCircle24,
            "已完成",
            "App.Brush.Success.Subtle",
            "App.Brush.Success",
            "当前章节的示例音频已准备就绪。"));
        content.Children.Add(CreateStatusRow(
            "component-status-view-warning",
            SymbolRegular.Warning24,
            "需要注意",
            "App.Brush.Warning.Subtle",
            "App.Brush.Warning",
            "部分内容仍在等待处理，状态文字不能只依赖颜色表达。"));
        content.Children.Add(CreateStatusRow(
            "component-status-view-error",
            SymbolRegular.DismissCircle24,
            "无法完成请求",
            "App.Brush.Danger.Subtle",
            "App.Brush.Danger",
            "这是用于布局契约的长错误说明：错误摘要保持简洁，详细文字可以换行并在 100%、125% 和 150% DPI 下完整显示。"));

        var surface = CreateComponentSurface("component-status-view");
        surface.Padding = TokenPadding("App.Space.20");
        surface.Child = content;
        return surface;
    }

    private static Border CreateStatusRow(
        string automationId,
        SymbolRegular symbol,
        string title,
        string backgroundKey,
        string accentKey,
        string description)
    {
        var icon = new SymbolIcon { Symbol = symbol, VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(FrameworkElement.WidthProperty, "App.Size.Icon.Standard");
        icon.SetResourceReference(FrameworkElement.HeightProperty, "App.Size.Icon.Standard");
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, accentKey);
        icon.Margin = new Thickness(0, 2, TokenDouble("App.Space.12"), 0);

        var copy = new StackPanel();
        copy.Children.Add(CreateTokenText(title, "App.Text.Size.ItemTitle", "App.Text.Weight.SemiBold", accentKey));
        var details = CreateTokenText(
            description,
            "App.Text.Size.Secondary",
            "App.Text.Weight.Regular",
            "App.Brush.Text.Secondary",
            "App.Text.LineHeight.Secondary");
        details.Margin = new Thickness(0, TokenDouble("App.Space.4"), 0, 0);
        AutomationProperties.SetAutomationId(details, $"{automationId}-description");
        copy.Children.Add(details);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(copy, 1);
        row.Children.Add(icon);
        row.Children.Add(copy);

        var surface = new Border
        {
            Padding = TokenPadding("App.Space.12"),
            Child = row
        };
        surface.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        surface.SetResourceReference(Border.CornerRadiusProperty, "App.Radius.Small");
        surface.Margin = new Thickness(0, TokenDouble("App.Space.12"), 0, 0);
        AutomationProperties.SetAutomationId(surface, automationId);
        return surface;
    }

    private static Border CreateComponentSurface(string automationId)
    {
        var surface = new Border
        {
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true
        };
        surface.SetResourceReference(Border.BackgroundProperty, "App.Brush.Surface.Primary");
        surface.SetResourceReference(Border.BorderBrushProperty, "App.Brush.Border.Subtle");
        surface.SetResourceReference(Border.CornerRadiusProperty, "App.Radius.Medium");
        surface.Margin = new Thickness(0, 0, 0, TokenDouble("App.Space.16"));
        AutomationProperties.SetAutomationId(surface, automationId);
        return surface;
    }

    private static TextBlock CreateTokenText(
        string text,
        string fontSizeKey,
        string fontWeightKey,
        string foregroundKey,
        string? lineHeightKey = null)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "App.Text.Family.Ui");
        block.SetResourceReference(TextBlock.FontSizeProperty, fontSizeKey);
        block.SetResourceReference(TextBlock.FontWeightProperty, fontWeightKey);
        block.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        if (lineHeightKey is not null)
        {
            block.SetResourceReference(TextBlock.LineHeightProperty, lineHeightKey);
        }

        return block;
    }

    private static double TokenDouble(string key) =>
        (double)(System.Windows.Application.Current?.FindResource(key)
            ?? throw new InvalidOperationException($"Gallery token '{key}' was not found."));

    private static Thickness TokenPadding(string key)
    {
        var value = TokenDouble(key);
        return new Thickness(value);
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
        swatch.SetResourceReference(Border.BorderBrushProperty, "App.Brush.Border.Subtle");
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

        control.Style = System.Windows.Application.Current?.FindResource(resourceKey) as Style
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

    private static T CreateInputControl<T>(
        T control,
        string styleKey,
        string automationId,
        string automationName)
        where T : Control
    {
        control.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        AutomationProperties.SetAutomationId(control, automationId);
        AutomationProperties.SetName(control, automationName);
        return control;
    }

    private static Border CreateInputField(string label, Control control, string? errorMessage = null)
    {
        var field = new Border { Margin = new Thickness(0, 16, 0, 0) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 6)
        }.WithResource(TextBlock.ForegroundProperty, "GallerySecondaryTextBrush"));
        stack.Children.Add(control);
        if (errorMessage is not null)
        {
            var error = new TextBlock
            {
                Text = errorMessage,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12
            }.WithResource(TextBlock.ForegroundProperty, "App.Brush.Danger");
            AutomationProperties.SetAutomationId(
                error,
                $"{AutomationProperties.GetAutomationId(control)}-error");
            stack.Children.Add(error);
        }

        field.Child = stack;
        return field;
    }

    private static void MarkInputValidationError(
        Control control,
        DependencyProperty property,
        string message)
    {
        var source = new GalleryValidationSource { Value = control.GetValue(property) };
        BindingOperations.SetBinding(
            control,
            property,
            new Binding(nameof(GalleryValidationSource.Value))
            {
                Source = source,
                Mode = BindingMode.OneWay
            });
        var expression = BindingOperations.GetBindingExpressionBase(control, property)
            ?? throw new InvalidOperationException(
                $"Could not create a validation binding for {AutomationProperties.GetAutomationId(control)}.");
        Validation.MarkInvalid(
            expression,
            new ValidationError(new ExceptionValidationRule(), expression)
            {
                ErrorContent = message
            });
    }

    private sealed class GalleryValidationSource
    {
        public object? Value { get; set; }
    }

    private static T WithResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
