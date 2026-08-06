using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using NovelSpeaker.App.Shared.Presentation.Controls.Forms;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using NovelSpeaker.StyleGallery;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SettingsFormControlTests
{
    [Fact]
    public void Settings_and_forms_templates_are_implicit_and_have_no_page_width_or_last_row_contract()
    {
        var root = LocateRepositoryRoot();
        var settings = XDocument.Load(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "ControlThemes",
            "Settings.xaml"));
        var forms = XDocument.Load(Path.Combine(
            root,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "ControlThemes",
            "Forms.xaml"));
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var styles = settings.Root!.Elements().Concat(forms.Root!.Elements())
            .Where(element => element.Name.LocalName == "Style")
            .ToArray();

        Assert.Equal(4, styles.Length);
        Assert.All(styles, style =>
        {
            Assert.Null(style.Attribute(xaml + "Key"));
            Assert.NotNull(style.Attribute("TargetType"));
            Assert.Contains(style.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        });
        Assert.DoesNotContain(
            settings.Descendants().Concat(forms.Descendants()),
            element => element.Name.LocalName == "Setter" &&
                       ((string?)element.Attribute("Property") is "Width" or "MaxWidth"));
        Assert.DoesNotContain(
            settings.ToString(),
            "LastRow",
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Settings_and_form_controls_apply_default_styles_preserve_slots_and_bindings(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var source = new BindingSource();
            var commandRow = new AppSettingsNavigationRow
            {
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                Title = "常规",
                Description = "打开常规设置。"
            };
            BindingOperations.SetBinding(
                commandRow,
                WpfButton.CommandProperty,
                new Binding(nameof(BindingSource.Command)));
            var emptyDescriptionNavigationRow = new AppSettingsNavigationRow
            {
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                Title = "无说明入口"
            };

            var value = new WpfTextBlock();
            BindingOperations.SetBinding(value, WpfTextBlock.TextProperty, new Binding(nameof(BindingSource.Value)));
            var settingsRow = new AppSettingsRow
            {
                Description = "设置项说明。",
                Value = value
            };
            BindingOperations.SetBinding(settingsRow, AppSettingsRow.TitleProperty, new Binding(nameof(BindingSource.Title)));
            var group = new AppSettingsGroup
            {
                Header = "设置",
                Description = "分组说明。",
                Footer = new WpfButton { Content = "应用" },
                DataContext = source
            };
            group.Items.Add(settingsRow);
            group.Items.Add(commandRow);
            group.Items.Add(emptyDescriptionNavigationRow);

            var formContent = new WpfTextBox { Text = "字段值" };
            var field = new AppFormField
            {
                Label = "字段",
                Description = "字段说明。",
                Required = true,
                Error = "字段错误。",
                Content = formContent
            };

            using var host = WpfWindowHost.Show(new Window
            {
                Content = new StackPanel
                {
                    Width = 720,
                    Children = { group, field }
                },
                Width = 800,
                Height = 600,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            Assert.NotNull(group.Style);
            Assert.NotNull(group.Template);
            Assert.NotNull(settingsRow.Template);
            Assert.NotNull(commandRow.Template);
            Assert.NotNull(field.Template);
            Assert.Equal(3, group.Items.Count);
            Assert.Same(value, settingsRow.Value);
            Assert.Equal(source.Title, settingsRow.Title);
            Assert.Equal(source.Value, value.Text);
            Assert.Same(formContent, field.Content);
            Assert.Same(source.Command, commandRow.Command);
            Assert.Equal(commandRow.Title, AutomationProperties.GetName(commandRow));
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<WpfTextBlock>(
                    emptyDescriptionNavigationRow.Template!.FindName("DescriptionPresenter", emptyDescriptionNavigationRow)).Visibility);
            Assert.Equal(
                VerticalAlignment.Center,
                Assert.IsType<StackPanel>(
                    emptyDescriptionNavigationRow.Template.FindName("CopyPresenter", emptyDescriptionNavigationRow)).VerticalAlignment);
            Assert.True(commandRow.Focusable);
            Assert.True(commandRow.IsTabStop);
            Assert.True(group.ActualWidth > 0);
            Assert.True(settingsRow.ActualHeight > 0);
            Assert.True(field.ActualHeight > 0);
            Assert.Contains(
                VisualTreeTestHelper.FindDescendants<Border>(group),
                border => border.BorderThickness.Bottom > 0);
            var lastItemContainer = VisualTreeTestHelper.FindDescendants<ContentControl>(group)
                .Single(container => ReferenceEquals(container.Content, emptyDescriptionNavigationRow));
            Assert.Equal(
                0,
                Assert.IsType<Border>(
                    lastItemContainer.Template!.FindName("ItemSurface", lastItemContainer)).BorderThickness.Bottom);

            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<ContentPresenter>(group.Template.FindName("FooterPresenter", group)).Visibility);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<ContentPresenter>(settingsRow.Template.FindName("ValuePresenter", settingsRow)).Visibility);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<WpfTextBlock>(field.Template.FindName("RequiredPresenter", field)).Visibility);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<WpfTextBlock>(field.Template.FindName("ErrorPresenter", field)).Visibility);
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Settings_and_form_controls_keep_long_copy_and_error_below_content_at_narrow_width(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var row = new AppSettingsRow
            {
                Title = "很长的设置标题应该自然换行而不挤压右侧控件",
                Description = "这段说明用于验证窄宽度下的文字布局。",
                Content = new WpfTextBox
                {
                    Text = "值",
                    Width = 120,
                    Style = (Style)global::System.Windows.Application.Current!.FindResource("App.Input.TextBox.Standard")
                }
            };
            var field = new AppFormField
            {
                Label = "服务地址",
                Description = "字段说明在窄宽度下应该继续占据独立行。",
                Error = "错误文案位于输入控件下方。",
                Content = new WpfTextBox
                {
                    Text = "地址",
                    Style = (Style)global::System.Windows.Application.Current!.FindResource("App.Input.TextBox.Standard")
                }
            };
            var emptyDescriptionRow = new AppSettingsRow
            {
                Title = "无说明设置项",
                Content = new WpfTextBox
                {
                    Text = "值",
                    Style = (Style)global::System.Windows.Application.Current!.FindResource("App.Input.TextBox.Compact")
                }
            };

            using var host = WpfWindowHost.Show(new Window
            {
                Content = new StackPanel
                {
                    Width = 360,
                    Children = { row, emptyDescriptionRow, field }
                },
                Width = 440,
                Height = 500,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            Assert.True(row.ActualWidth > 0);
            Assert.True(row.ActualHeight > 60);
            Assert.True(field.ActualHeight > 100);
            Assert.True(row.IsNarrowLayout);
            Assert.True(emptyDescriptionRow.IsNarrowLayout);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<Border>(row.Template!.FindName("InlineLayoutBorder", row)).Visibility);
            var title = Assert.IsType<WpfTextBlock>(row.Template.FindName("TitlePresenter", row));
            var valuePresenter = Assert.IsType<ContentPresenter>(row.Template.FindName("ValuePresenter", row));
            Assert.Equal(1, Grid.GetRow(valuePresenter));
            Assert.Equal(0, Grid.GetColumn(valuePresenter));
            var titleBounds = title.TransformToAncestor(row).TransformBounds(new Rect(new Point(), title.RenderSize));
            var valueBounds = valuePresenter.TransformToAncestor(row).TransformBounds(new Rect(new Point(), valuePresenter.RenderSize));
            Assert.True(titleBounds.Bottom <= valueBounds.Top);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsType<WpfTextBlock>(field.Template!.FindName("ErrorPresenter", field)).Visibility);
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<WpfTextBlock>(
                    emptyDescriptionRow.Template!.FindName("DescriptionPresenter", emptyDescriptionRow)).Visibility);
            Assert.Equal(
                VerticalAlignment.Center,
                Assert.IsType<StackPanel>(
                    emptyDescriptionRow.Template.FindName("CopyPresenter", emptyDescriptionRow)).VerticalAlignment);

            var boundaryNarrowRow = new AppSettingsRow
            {
                Width = 560,
                Title = "边界窄布局",
                Content = new WpfTextBlock { Text = "值" }
            };
            var boundaryWideRow = new AppSettingsRow
            {
                Width = 561,
                Title = "边界横向布局",
                Content = new WpfTextBlock { Text = "值" }
            };
            using var boundaryHost = WpfWindowHost.Show(new Window
            {
                Content = new StackPanel
                {
                    Children = { boundaryNarrowRow, boundaryWideRow }
                },
                Width = 700,
                Height = 220,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            boundaryHost.Window.UpdateLayout();
            Assert.True(boundaryNarrowRow.IsNarrowLayout);
            Assert.False(boundaryWideRow.IsNarrowLayout);
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Settings_and_form_gallery_scenes_render_required_fixture_families(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            var settingsScene = GallerySceneRegistry.Build("settings-controls");
            var formScene = GallerySceneRegistry.Build("form-field");
            using var settingsHost = WpfWindowHost.Show(new Window
            {
                Content = settingsScene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            using var formHost = WpfWindowHost.Show(new Window
            {
                Content = formScene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            settingsHost.Window.UpdateLayout();
            formHost.Window.UpdateLayout();

            Assert.NotEmpty(VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(settingsScene));
            Assert.NotEmpty(VisualTreeTestHelper.FindDescendants<AppSettingsRow>(settingsScene));
            Assert.NotEmpty(VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(settingsScene));
            Assert.True(VisualTreeTestHelper.FindDescendants<ToggleSwitch>(settingsScene).Count() > 0);
            Assert.True(VisualTreeTestHelper.FindDescendants<ComboBox>(settingsScene).Count() > 0);
            Assert.True(VisualTreeTestHelper.FindDescendants<WpfTextBox>(settingsScene).Count() > 0);
            Assert.True(VisualTreeTestHelper.FindDescendants<WpfButton>(settingsScene).Count() > 0);
            Assert.Equal(5, VisualTreeTestHelper.FindDescendants<AppFormField>(formScene).Count());
            Assert.Contains(
                VisualTreeTestHelper.FindDescendants<WpfTextBlock>(formScene),
                text => text.Text == "请输入可访问的服务地址。");
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Settings_and_form_gallery_scenes_keep_finite_layout_at_supported_dpi_scales(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                var settingsScene = GallerySceneRegistry.Build("settings-controls");
                var formScene = GallerySceneRegistry.Build("form-field");
                settingsScene.LayoutTransform = new ScaleTransform(scale, scale);
                formScene.LayoutTransform = new ScaleTransform(scale, scale);

                foreach (var scene in new[] { settingsScene, formScene })
                {
                    scene.Measure(new Size(GalleryRenderSettings.WindowWidth, GalleryRenderSettings.WindowHeight));
                    scene.Arrange(new Rect(0, 0, GalleryRenderSettings.WindowWidth, GalleryRenderSettings.WindowHeight));
                    scene.UpdateLayout();
                    Assert.All(
                        VisualTreeTestHelper.FindDescendants<FrameworkElement>(scene),
                        element =>
                        {
                            Assert.True(double.IsFinite(element.ActualWidth));
                            Assert.True(double.IsFinite(element.ActualHeight));
                            Assert.True(double.IsFinite(element.DesiredSize.Width));
                            Assert.True(double.IsFinite(element.DesiredSize.Height));
                        });
                }

                Assert.All(
                    VisualTreeTestHelper.FindDescendants<AppSettingsRow>(settingsScene),
                    row => Assert.True(row.ActualWidth > 0 && row.ActualHeight > 0));
                Assert.Contains(
                    VisualTreeTestHelper.FindDescendants<WpfTextBlock>(formScene),
                    text => text.Text == "请输入可访问的服务地址。" && text.ActualWidth > 0 && text.ActualHeight > 0);
            }
        });
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

    private sealed class BindingSource
    {
        public string Title { get; } = "设置项";

        public string Value { get; } = "只读值";

        public ICommand Command { get; } = new TestCommand();
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
