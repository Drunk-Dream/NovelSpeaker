using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NovelSpeaker.App.Shared.Presentation.Rules;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryRulesSharedScene
{
    private static readonly GalleryCommand FixtureCommand = new();

    public static FrameworkElement Create()
    {
        var root = new Grid
        {
            Width = GalleryRenderSettings.WindowWidth,
            Height = GalleryRenderSettings.WindowHeight,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        root.SetResourceReference(Panel.BackgroundProperty, "GalleryCanvasBackgroundBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(32, 28, 32, 20) };
        header.Children.Add(CreateText("Rules shared list items", "App.Typography.PageTitle"));
        header.Children.Add(CreateText(
            "正式共享控件覆盖三类规则、独立 Toggle、ContextMenu、键盘焦点、长按拖动反馈和中心线插入位置。",
            "App.Typography.Secondary",
            new Thickness(0, 6, 0, 0)));
        root.Children.Add(header);

        var columns = new Grid { Margin = new Thickness(32, 0, 32, 32) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(columns, 1);
        root.Children.Add(columns);

        var ttsColumn = CreateColumn(
            "TTS list states",
            "普通、禁用、Selected 与 Focus；TTS 不提供排序菜单。",
            CreateRule(
                "rules-shared-tts-default",
                "TTS",
                "标准语音服务",
                "POST · speech.example.test",
                isEnabled: true),
            CreateRule(
                "rules-shared-tts-disabled",
                "TTS",
                "已禁用的备用服务",
                "GET · backup.example.test",
                isEnabled: false),
            CreateRule(
                "rules-shared-tts-selected",
                "TTS",
                "正在编辑的服务",
                "POST · selected.example.test",
                isEnabled: true,
                isSelected: true),
            CreateFocusedRule());
        Grid.SetColumn(ttsColumn, 0);
        columns.Children.Add(ttsColumn);

        var sortableColumn = CreateColumn(
            "Sortable and menu states",
            "章节与正则支持备用排序；DragOver 只投影插入线，Drop 后才执行命令。",
            CreateRule(
                "rules-shared-chapter-sortable",
                "Chapter",
                "章节标题识别",
                @"^\s*第\s*\d+章",
                isEnabled: true,
                isSortable: true,
                canMoveUp: false),
            CreateRule(
                "rules-shared-regex-context-menu",
                "Regex",
                "ContextMenu · 右键 / Shift+F10",
                "空白折叠 → 单个空格",
                isEnabled: true,
                isSortable: true),
            CreateRule(
                "rules-shared-chapter-dragging",
                "Chapter",
                "长按 300 ms 后移动",
                "拖动态轻量反馈",
                isEnabled: true,
                isSortable: true,
                isDragging: true),
            CreateRule(
                "rules-shared-regex-insert-before",
                "Regex",
                "插入到目标之前",
                "目标中心线上方",
                isEnabled: true,
                isSortable: true,
                dropPlacement: RuleDropPlacement.Before),
            CreateRule(
                "rules-shared-regex-insert-after",
                "Regex",
                "插入到目标之后",
                "目标中心线下方",
                isEnabled: true,
                isSortable: true,
                dropPlacement: RuleDropPlacement.After));
        Grid.SetColumn(sortableColumn, 2);
        columns.Children.Add(sortableColumn);
        return root;
    }

    private static Border CreateColumn(
        string title,
        string description,
        params RuleListItemView[] rules)
    {
        var content = new StackPanel();
        content.Children.Add(CreateText(title, "App.Typography.SectionTitle"));
        content.Children.Add(CreateText(
            description,
            "App.Typography.Secondary",
            new Thickness(0, 4, 0, 12)));
        foreach (var rule in rules)
        {
            content.Children.Add(rule);
        }

        var surface = new Border
        {
            Padding = new Thickness(16),
            Style = FindStyle("App.Surface.Section"),
            Child = content
        };
        return surface;
    }

    private static RuleListItemView CreateFocusedRule()
    {
        var rule = CreateRule(
            "rules-shared-tts-focus",
            "TTS",
            "键盘焦点",
            "Menu Key 可打开同一菜单",
            isEnabled: true);
        rule.Loaded += (_, _) => rule.Dispatcher.BeginInvoke(
            () => rule.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)),
            DispatcherPriority.Input);
        return rule;
    }

    private static RuleListItemView CreateRule(
        string automationId,
        string family,
        string title,
        string summary,
        bool isEnabled,
        bool isSelected = false,
        bool isSortable = false,
        bool canMoveUp = true,
        bool isDragging = false,
        RuleDropPlacement dropPlacement = RuleDropPlacement.None)
    {
        var fixture = new GalleryRuleFixture(family, title, summary);
        var rule = new RuleListItemView
        {
            Title = title,
            Summary = summary,
            IsRuleEnabled = isEnabled,
            IsSelected = isSelected,
            IsSortable = isSortable,
            CanMoveUp = canMoveUp,
            IsDragging = isDragging,
            DropPlacement = dropPlacement,
            CommandParameter = fixture,
            SelectCommand = FixtureCommand,
            ToggleEnabledCommand = FixtureCommand,
            ExportCommand = FixtureCommand,
            CopyCommand = FixtureCommand,
            DeleteCommand = FixtureCommand,
            MoveUpCommand = FixtureCommand,
            MoveDownCommand = FixtureCommand,
            ReorderCommand = FixtureCommand
        };
        AutomationProperties.SetAutomationId(rule, automationId);
        AutomationProperties.SetName(rule, $"{family} fixture · {title}");
        AutomationProperties.SetHelpText(rule, family);
        return rule;
    }

    private static TextBlock CreateText(string text, string styleKey, Thickness? margin = null) =>
        new()
        {
            Text = text,
            Style = FindStyle(styleKey),
            Margin = margin ?? default,
            TextWrapping = TextWrapping.Wrap
        };

    private static Style FindStyle(string key) =>
        (Style)(System.Windows.Application.Current?.FindResource(key)
        ?? throw new InvalidOperationException($"Gallery resource '{key}' was not found."));

    private sealed record GalleryRuleFixture(string Family, string Title, string Summary);

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
