using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryCommonControlScenes
{
    public static FrameworkElement CreatePageHeader()
    {
        return CreateSceneRoot("page-header", CreatePageHeaderContent());
    }

    public static FrameworkElement CreateSectionSurface()
    {
        return CreateSceneRoot("section-surface", CreateSectionSurfaceContent());
    }

    public static FrameworkElement CreateStatusView()
    {
        return CreateSceneRoot("status-view", CreateStatusViewContent());
    }

    private static FrameworkElement CreatePageHeaderContent()
    {
        var content = new StackPanel();
        content.Children.Add(CreatePageHeaderControl(
            "page-header-no-back",
            "无返回入口的一级页面：这是一个很长的页面标题，用于验证标题省略和操作区布局",
            "页面说明可以自然换行，并在较窄的可用宽度与更高 DPI 下保持完整可读。",
            backCommand: null,
            actionText: "导入"));
        content.Children.Add(CreatePageHeaderControl(
            "page-header-with-back",
            "带返回入口的设置子页标题",
            "返回按钮、标题、副标题和操作区都属于同一个正式 PageHeader 控件。",
            new GalleryCommand(),
            actionText: "保存"));
        return content;
    }

    private static FrameworkElement CreateSectionSurfaceContent()
    {
        var content = new StackPanel();
        content.Children.Add(new AppSectionSurface
        {
            Header = "主要内容区块",
            Description = "区块标题和说明由控件模板排版，页面仍然拥有自己的列宽和滚动宿主。",
            Content = CreateSectionContent("section-surface-content-default"),
            Footer = CreateActionButton("section-surface-footer", "应用")
        }.WithAutomation("section-surface-default", "主要内容区块"));
        content.Children.Add(new AppSectionSurface
        {
            Header = "长标题与无 Footer",
            Description = "这段较长的说明用于验证标题换行、正文内容槽和窄宽度下的非零布局。",
            Content = CreateSectionContent("section-surface-content-long")
        }.WithAutomation("section-surface-long", "长标题与无 Footer"));
        return content;
    }

    private static FrameworkElement CreateStatusViewContent()
    {
        var content = new StackPanel();
        content.Children.Add(CreateStatus(
            "status-view-loading",
            AppStatusKind.Loading,
            SymbolRegular.ArrowSync24,
            "正在加载",
            "内容仍在准备中，请稍候。",
            "取消"));
        content.Children.Add(CreateStatus(
            "status-view-empty",
            AppStatusKind.Empty,
            SymbolRegular.Library24,
            "暂无内容",
            "导入一本书后，这里会显示可操作的内容。",
            "导入"));
        content.Children.Add(CreateStatus(
            "status-view-no-result",
            AppStatusKind.NoResult,
            SymbolRegular.Search24,
            "没有匹配结果",
            "调整筛选条件后再次尝试。",
            "清除筛选"));
        content.Children.Add(CreateStatus(
            "status-view-error",
            AppStatusKind.Error,
            SymbolRegular.Warning24,
            "加载失败",
            "这是用于布局契约的长错误说明：错误摘要保持简洁，详细文字可以换行并在 100%、125% 和 150% DPI 下完整显示。",
            "重试",
            "返回"));
        content.Children.Add(CreateStatus(
            "status-view-success",
            AppStatusKind.Success,
            SymbolRegular.CheckmarkCircle24,
            "已完成",
            "当前操作已经完成，可以继续处理后续内容。",
            "查看"));
        return content;
    }

    private static Grid CreateSceneRoot(string automationId, FrameworkElement content)
    {
        var root = new Grid
        {
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        root.SetResourceReference(Panel.BackgroundProperty, "GalleryCanvasBackgroundBrush");
        AutomationProperties.SetAutomationId(root, automationId);
        content.Margin = new Thickness(32, 32, 32, 32);
        root.Children.Add(content);
        return root;
    }

    private static AppPageHeader CreatePageHeaderControl(
        string automationId,
        string title,
        string description,
        ICommand? backCommand,
        string actionText)
    {
        var header = new AppPageHeader
        {
            Title = title,
            Description = description,
            BackCommand = backCommand,
            Actions = CreateActionButton($"{automationId}-action", actionText),
            Margin = new Thickness(0, 0, 0, 20)
        };
        header.SetAutomation(automationId, title);
        return header;
    }

    private static StackPanel CreateSectionContent(string automationId)
    {
        var content = new StackPanel();
        AutomationProperties.SetAutomationId(content, automationId);
        content.Children.Add(new WpfTextBlock
        {
            Text = "内容槽由调用方提供：这段固定 fixture 文本包含中文和 English copy，用于验证正式区块不会截断内容。",
            Style = FindStyle("App.Typography.Body")
        });
        content.Children.Add(new WpfTextBlock
        {
            Text = "辅助说明会在内容区域内自然换行。",
            Margin = new Thickness(0, 8, 0, 0),
            Style = FindStyle("App.Typography.Secondary")
        });
        return content;
    }

    private static AppStatusView CreateStatus(
        string automationId,
        AppStatusKind status,
        SymbolRegular icon,
        string title,
        string description,
        string primaryAction,
        string? secondaryAction = null)
    {
        var view = new AppStatusView
        {
            Status = status,
            Icon = icon,
            Title = title,
            Description = description,
            PrimaryAction = CreateActionButton($"{automationId}-primary", primaryAction),
            SecondaryAction = secondaryAction is null
                ? null
                : CreateActionButton($"{automationId}-secondary", secondaryAction),
            Margin = new Thickness(0, 0, 0, 12)
        };
        view.SetAutomation(automationId, title);
        return view;
    }

    private static WpfButton CreateActionButton(string automationId, string text)
    {
        var button = new WpfButton
        {
            Content = text,
            Style = FindStyle("App.Button.Secondary"),
            MinWidth = 84
        };
        button.SetAutomation(automationId, text);
        return button;
    }

    private static Style FindStyle(string key) =>
        System.Windows.Application.Current?.FindResource(key) as Style
        ?? throw new InvalidOperationException($"Gallery resource '{key}' was not found.");

    private static T WithAutomation<T>(this T element, string automationId, string name)
        where T : FrameworkElement
    {
        element.SetAutomation(automationId, name);
        return element;
    }

    private static void SetAutomation(this FrameworkElement element, string automationId, string name)
    {
        AutomationProperties.SetAutomationId(element, automationId);
        AutomationProperties.SetName(element, name);
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
