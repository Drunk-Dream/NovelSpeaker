using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.StyleGallery;
using Xunit;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CommonFeedbackControlTests
{
    [Fact]
    public void Common_and_feedback_templates_are_type_implicit_and_owned_by_their_dictionaries()
    {
        var root = LocateRepositoryRoot();
        var common = XDocument.Load(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "ControlThemes",
            "Common.xaml"));
        var feedback = XDocument.Load(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "ControlThemes",
            "Feedback.xaml"));

        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var commonStyles = common.Root?.Elements().Where(element => element.Name.LocalName == "Style").ToArray() ?? [];
        var feedbackStyles = feedback.Root?.Elements().Where(element => element.Name.LocalName == "Style").ToArray() ?? [];

        Assert.Equal(2, commonStyles.Length);
        Assert.Single(feedbackStyles);
        Assert.All(commonStyles.Concat(feedbackStyles), style =>
        {
            Assert.Null(style.Attribute(xaml + "Key"));
            Assert.NotNull(style.Attribute("TargetType"));
            Assert.Contains(
                style.Descendants(),
                element => element.Name.LocalName == "ControlTemplate");
        });
        Assert.DoesNotContain(
            commonStyles.Concat(feedbackStyles).SelectMany(style => style.Descendants()),
            element => element.Name.LocalName == "ScrollViewer");
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Common_controls_apply_implicit_styles_and_preserve_content_slots(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var noBack = new AppPageHeader
            {
                Title = "一级页面的长标题：用于检查自然省略而不是溢出窗口",
                Description = "这是页面说明，应该保持可读并在可用宽度不足时自然换行。",
                Actions = new WpfButton { Content = "操作" }
            };
            AutomationProperties.SetAutomationId(noBack, "control-page-header-no-back");
            AutomationProperties.SetName(noBack, noBack.Title);

            var withBack = new AppPageHeader
            {
                Title = "设置子页",
                Description = "带返回命令的页面标题。",
                BackCommand = new TestCommand(),
                Actions = new WpfButton { Content = "保存" }
            };
            AutomationProperties.SetAutomationId(withBack, "control-page-header-with-back");
            AutomationProperties.SetName(withBack, withBack.Title);

            var emptyDescriptionPageHeader = new AppPageHeader
            {
                Title = "无说明页面",
                Height = 80,
                Description = string.Empty
            };
            var nullDescriptionPageHeader = new AppPageHeader
            {
                Title = "空值说明页面",
                Description = null!
            };

            var section = new AppSectionSurface
            {
                Header = "区块标题",
                Description = "区块说明",
                Content = new WpfTextBlock { Text = "内容槽" },
                Footer = new WpfButton { Content = "应用" }
            };
            AutomationProperties.SetAutomationId(section, "control-section-surface");
            AutomationProperties.SetName(section, section.Header);

            var emptyDescriptionSection = new AppSectionSurface
            {
                Header = "无说明区块",
                Description = string.Empty,
                Content = new WpfTextBlock { Text = "内容槽" }
            };

            var boundActionsButton = new WpfButton { Content = "绑定动作" };
            var actionsBoundSection = new AppSectionSurface
            {
                Header = "绑定动作区块",
                DataContext = new ActionSource(boundActionsButton)
            };
            BindingOperations.SetBinding(
                actionsBoundSection,
                AppSectionSurface.ActionsProperty,
                new Binding(nameof(ActionSource.Value)));

            using var host = WpfWindowHost.Show(new Window
            {
                Content = new StackPanel
                {
                    Width = 720,
                    Children =
                    {
                        noBack,
                        withBack,
                        emptyDescriptionPageHeader,
                        nullDescriptionPageHeader,
                        section,
                        emptyDescriptionSection,
                        actionsBoundSection
                    }
                },
                Width = 800,
                Height = 600,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            Assert.NotNull(noBack.Style);
            Assert.NotNull(noBack.Template);
            Assert.NotNull(withBack.Template);
            Assert.NotNull(section.Template);

            var noBackButton = Assert.IsType<WpfButton>(
                noBack.Template!.FindName("BackButton", noBack));
            Assert.Equal(Visibility.Collapsed, noBackButton.Visibility);
            var noBackTitle = Assert.IsType<WpfTextBlock>(
                noBack.Template.FindName("TitlePresenter", noBack));
            Assert.Equal(TextTrimming.CharacterEllipsis, noBackTitle.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap, noBackTitle.TextWrapping);
            Assert.Equal(noBack.Title, noBackTitle.ToolTip);
            Assert.Equal(noBack.Title, AutomationProperties.GetName(noBackTitle));
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<WpfTextBlock>(
                    emptyDescriptionPageHeader.Template!.FindName("DescriptionPresenter", emptyDescriptionPageHeader)).Visibility);
            Assert.Equal(
                VerticalAlignment.Center,
                Assert.IsType<Grid>(
                    emptyDescriptionPageHeader.Template.FindName("TitleLayout", emptyDescriptionPageHeader)).VerticalAlignment);
            Assert.Equal(
                VerticalAlignment.Center,
                Assert.IsType<WpfButton>(
                    emptyDescriptionPageHeader.Template.FindName("BackButton", emptyDescriptionPageHeader)).VerticalAlignment);
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<WpfTextBlock>(
                    nullDescriptionPageHeader.Template!.FindName("DescriptionPresenter", nullDescriptionPageHeader)).Visibility);

            var backButton = Assert.IsType<WpfButton>(
                withBack.Template!.FindName("BackButton", withBack));
            Assert.Equal(Visibility.Visible, backButton.Visibility);
            Assert.True(backButton.Focusable);
            Assert.True(withBack.ActualWidth > 0);
            Assert.True(withBack.ActualHeight > 0);

            var header = Assert.IsType<WpfTextBlock>(
                section.Template!.FindName("HeaderPresenter", section));
            Assert.Equal(section.Header, header.Text);
            Assert.Equal(section.Header, AutomationProperties.GetName(header));
            Assert.Equal(Visibility.Visible,
                Assert.IsType<ContentPresenter>(section.Template.FindName("ContentPresenter", section)).Visibility);
            Assert.Equal(Visibility.Visible,
                Assert.IsType<ContentPresenter>(section.Template.FindName("FooterPresenter", section)).Visibility);
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<WpfTextBlock>(
                    emptyDescriptionSection.Template!.FindName("DescriptionPresenter", emptyDescriptionSection)).Visibility);
            Assert.Same(boundActionsButton, actionsBoundSection.Footer);
            Assert.Same(AppSectionSurface.FooterProperty, AppSectionSurface.ActionsProperty);
            Assert.True(section.ActualWidth > 0);
            Assert.True(section.ActualHeight > 0);
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Status_view_exposes_all_states_actions_and_usable_layout(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var statuses = new[]
            {
                AppStatusKind.Loading,
                AppStatusKind.Empty,
                AppStatusKind.NoResult,
                AppStatusKind.Error,
                AppStatusKind.Success
            }.Select((status, index) =>
            {
                var view = new AppStatusView
                {
                    Status = status,
                    Icon = Wpf.Ui.Controls.SymbolRegular.Info24,
                    Title = $"状态 {index}",
                    Description = "状态说明必须保留非零布局，并且不能只依赖颜色表达。",
                    PrimaryAction = new WpfButton { Content = "主要操作" },
                    SecondaryAction = index == 3 ? new WpfButton { Content = "次要操作" } : null,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                AutomationProperties.SetAutomationId(view, $"control-status-{index}");
                AutomationProperties.SetName(view, view.Title);
                return view;
            }).ToArray();

            using var host = WpfWindowHost.Show(new Window
            {
                Content = new StackPanel
                {
                    Width = 720,
                    Children = { statuses[0], statuses[1], statuses[2], statuses[3], statuses[4] }
                },
                Width = 800,
                Height = 700,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            Assert.All(statuses, view =>
            {
                Assert.NotNull(view.Style);
                Assert.NotNull(view.Template);
                Assert.True(view.ActualWidth > 0);
                Assert.True(view.ActualHeight > 0);
                Assert.Equal(
                    view.Title,
                    AutomationProperties.GetName(
                        Assert.IsType<WpfTextBlock>(view.Template!.FindName("TitlePresenter", view))));
                Assert.NotNull(view.Template.FindName("IconPresenter", view));
            });

            var loading = statuses[0];
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<ContentPresenter>(
                    loading.Template!.FindName("PrimaryActionPresenter", loading)).Visibility);
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<ContentPresenter>(
                    loading.Template.FindName("SecondaryActionPresenter", loading)).Visibility);

            var error = statuses[3];
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<ContentPresenter>(
                    error.Template!.FindName("SecondaryActionPresenter", error)).Visibility);
            Assert.NotEqual(
                Colors.Transparent,
                Assert.IsType<SolidColorBrush>(
                    Assert.IsType<Border>(error.Template.FindName("IconSurface", error)).Background).Color);

            var success = statuses[4];
            Assert.Equal(
                Assert.IsType<SolidColorBrush>(
                    global::System.Windows.Application.Current!.FindResource("App.Brush.Success.Subtle")).Color,
                Assert.IsType<SolidColorBrush>(
                    Assert.IsType<Border>(success.Template!.FindName("IconSurface", success)).Background).Color);

            var emptyDescription = new AppStatusView
            {
                Title = "无说明状态",
                Description = string.Empty
            };
            using var emptyHost = WpfWindowHost.Show(new Window
            {
                Content = emptyDescription,
                Width = 400,
                Height = 200,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            emptyHost.Window.UpdateLayout();
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<WpfTextBlock>(
                    emptyDescription.Template!.FindName("DescriptionPresenter", emptyDescription)).Visibility);
            Assert.Equal(
                VerticalAlignment.Center,
                Assert.IsType<StackPanel>(
                    emptyDescription.Template.FindName("CopyPresenter", emptyDescription)).VerticalAlignment);
            Assert.Equal(
                VerticalAlignment.Center,
                Assert.IsType<Border>(
                    emptyDescription.Template.FindName("IconSurface", emptyDescription)).VerticalAlignment);
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Gallery_scenes_use_formal_controls_and_cover_required_variants(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var pageHeaderScene = GallerySceneRegistry.Build("page-header");
            var sectionScene = GallerySceneRegistry.Build("section-surface");
            var statusScene = GallerySceneRegistry.Build("status-view");
            using var pageHost = WpfWindowHost.Show(new Window
            {
                Content = pageHeaderScene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            using var sectionHost = WpfWindowHost.Show(new Window
            {
                Content = sectionScene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            using var statusHost = WpfWindowHost.Show(new Window
            {
                Content = statusScene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            pageHost.Window.UpdateLayout();
            sectionHost.Window.UpdateLayout();
            statusHost.Window.UpdateLayout();

            var headers = FindDescendants<AppPageHeader>(pageHeaderScene);
            Assert.Equal(2, headers.Count);
            Assert.Contains(headers, header =>
                Assert.IsType<WpfButton>(header.Template!.FindName("BackButton", header)).Visibility == Visibility.Collapsed);
            Assert.Contains(headers, header =>
                Assert.IsType<WpfButton>(header.Template!.FindName("BackButton", header)).Visibility == Visibility.Visible);

            var sections = FindDescendants<AppSectionSurface>(sectionScene);
            Assert.Equal(2, sections.Count);
            Assert.All(sections, section =>
            {
                Assert.NotNull(section.Style);
                Assert.True(section.ActualWidth > 0);
                Assert.True(section.ActualHeight > 0);
            });

            var statuses = FindDescendants<AppStatusView>(statusScene);
            Assert.Equal(5, statuses.Count);
            Assert.Equal(
                new[]
                {
                    AppStatusKind.Loading,
                    AppStatusKind.Empty,
                    AppStatusKind.NoResult,
                    AppStatusKind.Error,
                    AppStatusKind.Success
                },
                statuses.Select(status => status.Status));
            Assert.All(statuses, status =>
            {
                Assert.NotNull(status.Style);
                Assert.True(status.ActualWidth > 0);
                Assert.True(status.ActualHeight > 0);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(status)));
            });
        });
    }

    [Fact]
    public void Production_common_feedback_controls_do_not_contain_gallery_fixture_content()
    {
        var root = LocateRepositoryRoot();
        foreach (var relativePath in new[]
                 {
                     "src/NovelSpeaker.App/Shared/Presentation/Controls/Common/AppPageHeader.cs",
                     "src/NovelSpeaker.App/Shared/Presentation/Controls/Common/AppSectionSurface.cs",
                     "src/NovelSpeaker.App/Shared/Presentation/Controls/Feedback/AppStatusView.cs"
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("Style Gallery", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AutomationProperties", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Content =", source, StringComparison.Ordinal);
        }
    }

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

    private sealed class ActionSource
    {
        public ActionSource(object value)
        {
            Value = value;
        }

        public object Value { get; }
    }

    private sealed class TestCommand : ICommand
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
