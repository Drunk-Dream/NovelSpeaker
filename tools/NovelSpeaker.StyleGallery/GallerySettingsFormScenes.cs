using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.Shared.Presentation.Controls.Forms;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace NovelSpeaker.StyleGallery;

internal static class GallerySettingsFormScenes
{
    public static FrameworkElement CreateSettingsControls() =>
        CreateSceneRoot(
            "settings-controls",
            "Settings controls",
            "Settings home groups keep Primary Surface and row separators while standalone flat subpage rows share the same row contract without group wrappers.",
            CreateSettingsContent());

    public static FrameworkElement CreateFormField() =>
        CreateSceneRoot(
            "form-field",
            "Form field",
            "FormField owns labels, descriptions, required markers and error projection while the caller owns input types and business validation.",
            CreateFormContent());

    private static FrameworkElement CreateSettingsContent()
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var primaryGroup = new AppSettingsGroup
        {
            Header = "常用设置",
            Description = "不同类型的右侧内容共享同一行结构，说明文字可在较窄宽度下换行。"
        };
        primaryGroup.WithAutomation("settings-controls-primary", "常用设置");
        primaryGroup.Items.Add(CreateSettingsRow(
            "settings-controls-toggle",
            "朗读章节标题",
            "开启后，每章正文前先朗读章节标题。",
            new ToggleSwitch
            {
                IsChecked = true,
                OnContent = "开启",
                OffContent = "关闭",
                Style = FindStyle("App.Input.ToggleSwitch.Standard")
            }));
        primaryGroup.Items.Add(CreateSettingsRow(
            "settings-controls-combo",
            "应用主题",
            "选择跟随系统或固定主题。",
            new WpfComboBox
            {
                ItemsSource = new[] { "跟随系统", "浅色", "深色" },
                SelectedIndex = 0,
                Style = FindStyle("App.Input.ComboBox.Standard")
            }));
        primaryGroup.Items.Add(CreateSettingsRow(
            "settings-controls-textbox",
            "书名模板",
            "较长的说明会与输入框保持清晰间距。",
            new WpfTextBox
            {
                Text = "{{name}} · {{author}}",
                Style = FindStyle("App.Input.TextBox.Standard")
            }));
        var primaryStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        primaryStack.Children.Add(primaryGroup);

        var singleRowGroup = new AppSettingsGroup
        {
            Header = "单行分组",
            Description = "单行分组保持同一表面与无外框规则，行分隔线只在多行组内出现。",
            Margin = new Thickness(0, 16, 0, 0)
        };
        singleRowGroup.WithAutomation("settings-controls-single-row", "单行分组");
        singleRowGroup.Items.Add(CreateSettingsRow(
            "settings-controls-single-row-toggle",
            "启动时检查更新",
            "开关位于右侧，标题与分组 Header 保持统一左侧基线。",
            new ToggleSwitch
            {
                IsChecked = false,
                OnContent = "开启",
                OffContent = "关闭",
                Style = FindStyle("App.Input.ToggleSwitch.Standard")
            }));
        primaryStack.Children.Add(singleRowGroup);
        content.Children.Add(primaryStack);

        var secondaryStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        var secondaryGroup = new AppSettingsGroup
        {
            Header = "其它内容",
            Description = "按钮、只读值和整行导航都使用正式内容槽。"
        };
        secondaryGroup.WithAutomation("settings-controls-secondary", "其它内容");
        secondaryGroup.Items.Add(CreateSettingsRow(
            "settings-controls-button",
            "打开数据目录",
            "按钮命令由调用方提供。",
            CreateButton("settings-controls-open", "打开")));
        secondaryGroup.Items.Add(CreateSettingsRow(
            "settings-controls-readonly",
            "当前版本",
            "只读值不要求控件层保存业务状态。",
            new WpfTextBlock
            {
                Text = "0.1.0-preview",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                Style = FindStyle("App.Typography.Body")
            }));
        secondaryGroup.Items.Add(new AppSettingsNavigationRow
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
            Title = "更多设置",
            Description = "整行可点击，支持鼠标悬停、按下和键盘焦点。",
            Command = new GalleryCommand()
        }.WithAutomation("settings-controls-navigation", "更多设置"));
        secondaryStack.Children.Add(secondaryGroup);

        var narrowGroup = new AppSettingsGroup
        {
            Header = "窄宽度",
            Description = "这一组故意限制宽度，用于验证长标题和右侧控件不会重叠。",
            Width = 360,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        narrowGroup.WithAutomation("settings-controls-narrow", "窄宽度");
        narrowGroup.Items.Add(CreateSettingsRow(
            "settings-controls-narrow-row",
            "一段较长的设置标题会自然换行",
            "说明文字在 100%、125% 和 150% DPI 下均保留可读间距。",
            new WpfTextBox
            {
                Text = "自适应",
                Style = FindStyle("App.Input.TextBox.Compact")
            }));
        secondaryStack.Children.Add(narrowGroup);
        content.Children.Add(secondaryStack);
        Grid.SetColumn(secondaryStack, 1);

        var flatList = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        flatList.WithAutomation("settings-controls-flat-list", "扁平设置行");
        flatList.Children.Add(CreateSettingsRow(
            "settings-controls-flat-combo",
            "应用主题",
            "子页面中设置行直接位于扁平列表，不再显示“主题”分组标题。",
            new WpfComboBox
            {
                ItemsSource = new[] { "跟随系统", "浅色", "深色" },
                SelectedIndex = 0,
                Style = FindStyle("App.Input.ComboBox.Standard")
            }));
        flatList.Children.Add(CreateSettingsRow(
            "settings-controls-flat-toggle",
            "启动后最小化到托盘",
            "ToggleSwitch 与标题共享同一行几何，行与行之间只保留稳定间距。",
            new ToggleSwitch
            {
                IsChecked = true,
                OnContent = "开启",
                OffContent = "关闭",
                Style = FindStyle("App.Input.ToggleSwitch.Standard")
            }));
        var narrowFlatRows = new StackPanel
        {
            Width = 360,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 16, 0, 0)
        };
        narrowFlatRows.WithAutomation("settings-controls-flat-narrow-list", "窄扁平设置行");
        narrowFlatRows.Children.Add(CreateSettingsRow(
            "settings-controls-flat-narrow",
            "一段较长的扁平设置标题会自然换行",
            "独立行在窄宽度与 100/125/150% DPI 下仍与右侧控件保持垂直排列。",
            new WpfTextBox
            {
                Text = "自适应",
                Style = FindStyle("App.Input.TextBox.Compact")
            }));
        flatList.Children.Add(narrowFlatRows);
        content.Children.Add(flatList);
        Grid.SetRow(flatList, 1);
        Grid.SetColumnSpan(flatList, 2);
        return content;
    }

    private static FrameworkElement CreateFormContent()
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        left.Children.Add(CreateFormFieldControl(
            "form-field-required",
            "规则名称",
            "必填字段的标记由控件展示，业务验证仍由调用方负责。",
            new WpfTextBox
            {
                Text = "默认规则",
                Style = FindStyle("App.Input.TextBox.Standard")
            },
            required: true));
        left.Children.Add(CreateFormFieldControl(
            "form-field-combo",
            "请求方式",
            "字段可以承载 ComboBox 等不同输入类型。",
            new WpfComboBox
            {
                ItemsSource = new[] { "GET", "POST", "PUT" },
                SelectedIndex = 1,
                Style = FindStyle("App.Input.ComboBox.Standard")
            }));
        left.Children.Add(CreateFormFieldControl(
            "form-field-readonly",
            "规则标识",
            "只读输入仍由页面决定其编辑策略。",
            new WpfTextBox
            {
                Text = "builtin-default",
                IsReadOnly = true,
                Style = FindStyle("App.Input.TextBox.Standard")
            }));
        content.Children.Add(left);

        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        right.Children.Add(CreateFormFieldControl(
            "form-field-error",
            "服务地址",
            "错误文案位于字段下方，不依赖输入框边框颜色传达信息。",
            new WpfTextBox
            {
                Text = "https://example.invalid/tts",
                Style = FindStyle("App.Input.TextBox.Standard")
            },
            error: "请输入可访问的服务地址。"));
        right.Children.Add(CreateFormFieldControl(
            "form-field-long-description",
            "请求说明",
            "这是一段较长的字段说明，用于验证说明、输入控件和错误文案在窄宽度和 150% DPI 下保持垂直间距，不被截断，也不与相邻内容重叠。",
            new WpfTextBox
            {
                Text = "请求体中的正文占位符",
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MinHeight = 64,
                Style = FindStyle("App.Input.TextBox.Standard")
            }));
        content.Children.Add(right);
        Grid.SetColumn(right, 1);
        return content;
    }

    private static AppSettingsRow CreateSettingsRow(
        string automationId,
        string title,
        string description,
        FrameworkElement value)
    {
        return new AppSettingsRow
        {
            Title = title,
            Description = description,
            Content = value
        }.WithAutomation(automationId, title);
    }

    private static AppFormField CreateFormFieldControl(
        string automationId,
        string label,
        string description,
        FrameworkElement content,
        bool required = false,
        string? error = null)
    {
        return new AppFormField
        {
            Label = label,
            Description = description,
            Required = required,
            Error = error ?? string.Empty,
            Content = content,
            Margin = new Thickness(0, 0, 0, 18)
        }.WithAutomation(automationId, label);
    }

    private static WpfButton CreateButton(string automationId, string text)
    {
        var button = new WpfButton
        {
            Content = text,
            Style = FindStyle("App.Button.Secondary"),
            MinWidth = 84
        };
        return button.WithAutomation(automationId, text);
    }

    private static FrameworkElement CreateSceneRoot(
        string automationId,
        string title,
        string description,
        FrameworkElement content)
    {
        var root = new Grid
        {
            Width = GalleryRenderSettings.WindowWidth,
            Height = GalleryRenderSettings.WindowHeight,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        root.SetResourceReference(Panel.BackgroundProperty, "GalleryCanvasBackgroundBrush");
        AutomationProperties.SetAutomationId(root, automationId);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(32, 28, 32, 20) };
        header.Children.Add(new WpfTextBlock
        {
            Text = title,
            Style = FindStyle("App.Typography.PageTitle")
        });
        header.Children.Add(new WpfTextBlock
        {
            Text = description,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Style = FindStyle("App.Typography.Secondary")
        });
        root.Children.Add(header);

        content.Margin = new Thickness(32, 0, 32, 32);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        return root;
    }

    private static Style FindStyle(string key) =>
        System.Windows.Application.Current?.FindResource(key) as Style
        ?? throw new InvalidOperationException($"Gallery resource '{key}' was not found.");

    private static T WithAutomation<T>(this T element, string automationId, string name)
        where T : FrameworkElement
    {
        AutomationProperties.SetAutomationId(element, automationId);
        AutomationProperties.SetName(element, name);
        return element;
    }

    private sealed class GalleryCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
