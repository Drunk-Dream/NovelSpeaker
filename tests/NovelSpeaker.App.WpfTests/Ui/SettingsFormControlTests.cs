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
        var implicitStyles = styles
            .Where(style => style.Attribute(xaml + "Key") is null)
            .ToArray();

        Assert.Equal(6, styles.Length);
        Assert.Equal(5, implicitStyles.Length);
        Assert.All(implicitStyles, style =>
        {
            Assert.NotNull(style.Attribute("TargetType"));
            Assert.Contains(style.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        });
        var listSurfaceStyle = Assert.Single(
            styles,
            style => (string?)style.Attribute(xaml + "Key") == "App.Settings.ListSurface");
        Assert.Equal("Border", listSurfaceStyle.Attribute("TargetType")?.Value);
        Assert.DoesNotContain(
            settings.Descendants().Concat(forms.Descendants()),
            element => element.Name.LocalName == "Setter" &&
                       ((string?)element.Attribute("Property") is "Width" or "MaxWidth"));
        Assert.DoesNotContain(
            settings.ToString(),
            "LastRow",
            StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_and_form_controls_apply_default_styles_preserve_slots_and_bindings()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Settings_and_form_controls_apply_default_styles_preserve_slots_and_bindings_for_theme(theme);
        }
    }

    private void Settings_and_form_controls_apply_default_styles_preserve_slots_and_bindings_for_theme(GalleryTheme theme)
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

    [Fact]
    public void Settings_and_form_controls_keep_long_copy_and_error_below_content_at_narrow_width()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Settings_and_form_controls_keep_long_copy_and_error_below_content_at_narrow_width_for_theme(theme);
        }
    }

    private void Settings_and_form_controls_keep_long_copy_and_error_below_content_at_narrow_width_for_theme(GalleryTheme theme)
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

    [Fact]
    public void Settings_and_form_gallery_scenes_render_required_fixture_families()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Settings_and_form_gallery_scenes_render_required_fixture_families_for_theme(theme);
        }
    }

    private void Settings_and_form_gallery_scenes_render_required_fixture_families_for_theme(GalleryTheme theme)
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
            Assert.Contains(
                VisualTreeTestHelper.FindDescendants<AppSettingsList>(settingsScene),
                list => list.GetType() == typeof(AppSettingsList));
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

    [Fact]
    public void Settings_and_form_gallery_scenes_keep_finite_layout_at_supported_dpi_scales()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Settings_and_form_gallery_scenes_keep_finite_layout_at_supported_dpi_scales_for_theme(theme);
        }
    }

    private void Settings_and_form_gallery_scenes_keep_finite_layout_at_supported_dpi_scales_for_theme(GalleryTheme theme)
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

    [Fact]
    public void Settings_gallery_pairs_home_groups_with_headerless_settings_lists()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Settings_gallery_pairs_home_groups_with_headerless_settings_lists_for_theme(theme);
        }
    }

    private void Settings_gallery_pairs_home_groups_with_headerless_settings_lists_for_theme(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                var scene = GallerySceneRegistry.Build("settings-controls");
                scene.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = WpfWindowHost.Show(new Window
                {
                    Content = scene,
                    Width = GalleryRenderSettings.WindowWidth,
                    Height = GalleryRenderSettings.WindowHeight,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow
                });
                host.Window.UpdateLayout();

                Assert.Equal(
                    4,
                    VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(scene).ToArray().Length);

                var flatRows = VisualTreeTestHelper.FindDescendants<AppSettingsRow>(scene)
                    .Where(row => AutomationProperties.GetAutomationId(row)?.StartsWith(
                        "settings-controls-flat-",
                        StringComparison.Ordinal) == true)
                    .ToArray();
                Assert.Equal(3, flatRows.Length);
                Assert.All(flatRows, row =>
                {
                    Assert.False(HasGroupAncestor(row));
                    Assert.False(row.Focusable);
                    Assert.False(row.IsTabStop);
                    Assert.Equal(row.Title, AutomationProperties.GetName(row));
                    Assert.True(row.ActualWidth > 0);
                    Assert.True(row.ActualHeight >= 60);
                });

                var flatList = VisualTreeTestHelper.FindDescendants<AppSettingsList>(scene)
                    .Single(list =>
                        list.GetType() == typeof(AppSettingsList) &&
                        AutomationProperties.GetAutomationId(list) == "settings-controls-flat-list");
                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(flatList));
                Assert.DoesNotContain(
                    VisualTreeTestHelper.FindDescendants<WpfTextBlock>(flatList),
                    textBlock => ReferenceEquals(
                        textBlock.Style,
                        scene.FindResource("App.Typography.GroupTitle")));
                var listSurface = Assert.IsType<Border>(VisualTreeHelper.GetChild(flatList, 0));
                Assert.Same(flatList.FindResource("App.Brush.Surface.Primary"), listSurface.Background);
                Assert.Equal(
                    (CornerRadius)flatList.FindResource("App.Radius.Medium"),
                    listSurface.CornerRadius);
                Assert.Equal(new Thickness(20), listSurface.Padding);
                var flatContainers = Enumerable.Range(0, flatList.Items.Count)
                    .Select(index => Assert.IsType<ContentControl>(
                        flatList.ItemContainerGenerator.ContainerFromIndex(index)))
                    .ToArray();
                Assert.Equal(2, flatContainers.Length);
                Assert.True(
                    Assert.IsType<Border>(
                        flatContainers[0].Template!.FindName("ItemSurface", flatContainers[0]))
                    .BorderThickness.Bottom > 0);
                Assert.Equal(
                    0,
                    Assert.IsType<Border>(
                        flatContainers[1].Template!.FindName("ItemSurface", flatContainers[1]))
                    .BorderThickness.Bottom);

                var wideComboRow = flatRows.Single(row =>
                    AutomationProperties.GetAutomationId(row) == "settings-controls-flat-combo");
                var wideToggleRow = flatRows.Single(row =>
                    AutomationProperties.GetAutomationId(row) == "settings-controls-flat-toggle");
                Assert.False(wideComboRow.IsNarrowLayout);
                Assert.False(wideToggleRow.IsNarrowLayout);
                AssertControlDoesNotOverlapTitle(
                    wideComboRow,
                    "应用主题",
                    VisualTreeTestHelper.FindDescendants<ComboBox>(wideComboRow).Single());
                AssertControlDoesNotOverlapTitle(
                    wideToggleRow,
                    "启动后最小化到托盘",
                    VisualTreeTestHelper.FindDescendants<ToggleSwitch>(wideToggleRow).Single());

                var narrowRow = flatRows.Single(row =>
                    AutomationProperties.GetAutomationId(row) == "settings-controls-flat-narrow");
                Assert.True(narrowRow.IsNarrowLayout);
                AssertControlBelowTitle(
                    narrowRow,
                    "一段较长的设置标题会自然换行",
                    VisualTreeTestHelper.FindDescendants<WpfTextBox>(narrowRow).Single());
            }
        });
    }

    [Fact]
    public void Settings_group_geometry_keeps_baseline_and_controls_at_supported_dpi_scales()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Settings_group_geometry_keeps_baseline_and_controls_at_supported_dpi_scales_for_theme(theme);
        }
    }

    private void Settings_group_geometry_keeps_baseline_and_controls_at_supported_dpi_scales_for_theme(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);

            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                var toggle = new ToggleSwitch
                {
                    IsChecked = true,
                    Style = FindStyle("App.Input.ToggleSwitch.Standard")
                };
                var comboBox = new ComboBox
                {
                    ItemsSource = new[] { "跟随系统", "浅色", "深色" },
                    SelectedIndex = 0,
                    Style = FindStyle("App.Input.ComboBox.Standard")
                };
                var textBox = new WpfTextBox
                {
                    Text = "模板值",
                    Style = FindStyle("App.Input.TextBox.Standard")
                };
                var narrowTextBox = new WpfTextBox
                {
                    Text = "自适应",
                    Style = FindStyle("App.Input.TextBox.Compact")
                };

                var mainGroup = new AppSettingsGroup
                {
                    Header = "常用设置",
                    Description = "这是一个较长的分组说明，用于验证分组 Header 与行标题保持同一左侧基线。",
                    Width = 1000,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                mainGroup.Items.Add(new AppSettingsRow
                {
                    Title = "朗读章节标题",
                    Description = "开启后每章正文前先朗读章节标题。",
                    Content = toggle
                });
                mainGroup.Items.Add(new AppSettingsRow
                {
                    Title = "应用主题",
                    Description = "选择跟随系统或固定主题。",
                    Content = comboBox
                });
                mainGroup.Items.Add(new AppSettingsRow
                {
                    Title = "书名模板",
                    Description = "这是一段特别长的设置行说明，用于验证说明文字在较窄宽度下换行后仍然不会与右侧输入控件重叠，也不会因为多行而丢失行级密度。说明需要跨越至少两行才能覆盖多行场景，因此这里继续补充更多文字来确保在 100%、125% 和 150% DPI 下都能稳定地验证垂直间距与右侧控件边界。",
                    Content = textBox
                });

                var singleRowGroup = new AppSettingsGroup
                {
                    Header = "单行分组",
                    Width = 1000,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 16, 0, 0)
                };
                singleRowGroup.Items.Add(new AppSettingsRow
                {
                    Title = "启动时检查更新",
                    Content = new ToggleSwitch
                    {
                        Style = FindStyle("App.Input.ToggleSwitch.Standard")
                    }
                });

                var narrowGroup = new AppSettingsGroup
                {
                    Header = "窄宽度",
                    Width = 360,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 16, 0, 0)
                };
                var narrowRow = new AppSettingsRow
                {
                    Title = "一段较长的设置标题会自然换行",
                    Description = "说明文字在 100%、125% 和 150% DPI 下均保留可读间距。",
                    Content = narrowTextBox
                };
                narrowGroup.Items.Add(narrowRow);

                var root = new StackPanel
                {
                    LayoutTransform = new ScaleTransform(scale, scale)
                };
                root.Children.Add(mainGroup);
                root.Children.Add(singleRowGroup);
                root.Children.Add(narrowGroup);

                using var host = WpfWindowHost.Show(new Window
                {
                    Content = root,
                    Width = 1700,
                    Height = 1400,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow
                });
                host.Window.UpdateLayout();

                var groupSurface = Assert.IsType<Border>(VisualTreeHelper.GetChild(mainGroup, 0));
                Assert.Equal(new Thickness(0), groupSurface.BorderThickness);
                Assert.Same(mainGroup.FindResource("App.Brush.Surface.Primary"), groupSurface.Background);
                Assert.Equal(
                    (CornerRadius)mainGroup.FindResource("App.Radius.Medium"),
                    groupSurface.CornerRadius);
                Assert.Equal(new Thickness(20), groupSurface.Padding);

                var header = Assert.IsType<WpfTextBlock>(
                    mainGroup.Template!.FindName("HeaderPresenter", mainGroup));
                Assert.Same(mainGroup.FindResource("App.Typography.GroupTitle"), header.Style);

                var containers = Enumerable.Range(0, mainGroup.Items.Count)
                    .Select(index => Assert.IsType<ContentControl>(
                        mainGroup.ItemContainerGenerator.ContainerFromIndex(index)))
                    .ToArray();
                Assert.Equal(3, containers.Length);
                foreach (var container in containers)
                {
                    var itemSurface = Assert.IsType<Border>(
                        container.Template!.FindName("ItemSurface", container));
                    Assert.Equal(new Thickness(0), itemSurface.Padding);
                }

                var separatorBottoms = containers
                    .Select(container => Assert.IsType<Border>(
                            container.Template!.FindName("ItemSurface", container))
                        .BorderThickness.Bottom)
                    .ToArray();
                Assert.True(separatorBottoms[0] > 0);
                Assert.True(separatorBottoms[1] > 0);
                Assert.Equal(0, separatorBottoms[2]);

                var firstRow = Assert.IsType<AppSettingsRow>(containers[0].Content);
                var inlineBorder = Assert.IsType<Border>(
                    firstRow.Template!.FindName("InlineLayoutBorder", firstRow));
                Assert.Equal(0, inlineBorder.Padding.Left);
                Assert.Equal(0, inlineBorder.Padding.Right);
                Assert.True(inlineBorder.Padding.Top > 0);
                Assert.True(inlineBorder.Padding.Bottom > 0);

                var headerBounds = header.TransformToAncestor(mainGroup)
                    .TransformBounds(new Rect(header.RenderSize));
                var firstTitle = Assert.IsType<WpfTextBlock>(
                    firstRow.Template.FindName("TitlePresenter", firstRow));
                var firstTitleBounds = firstTitle.TransformToAncestor(mainGroup)
                    .TransformBounds(new Rect(firstTitle.RenderSize));
                Assert.InRange(
                    Math.Abs(headerBounds.Left - firstTitleBounds.Left),
                    0,
                    1);

                AssertGroupRowDoesNotOverlap(mainGroup, containers[0], toggle);
                AssertGroupRowDoesNotOverlap(mainGroup, containers[1], comboBox);
                AssertGroupRowDoesNotOverlap(mainGroup, containers[2], textBox);
                Assert.True(
                    Assert.IsType<AppSettingsRow>(containers[2].Content).ActualHeight >
                    Assert.IsType<AppSettingsRow>(containers[0].Content).ActualHeight,
                    "The long-description row should render taller than the single-line row.");

                var singleContainer = Assert.IsType<ContentControl>(
                    singleRowGroup.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.Equal(
                    0,
                    Assert.IsType<Border>(
                        singleContainer.Template!.FindName("ItemSurface", singleContainer))
                        .BorderThickness.Bottom);

                Assert.True(narrowRow.IsNarrowLayout);
                var narrowTitle = Assert.IsType<WpfTextBlock>(
                    narrowRow.Template!.FindName("TitlePresenter", narrowRow));
                var narrowTitleBounds = narrowTitle.TransformToAncestor(narrowRow)
                    .TransformBounds(new Rect(new Point(), narrowTitle.RenderSize));
                var narrowValueBounds = narrowTextBox.TransformToAncestor(narrowRow)
                    .TransformBounds(new Rect(new Point(), narrowTextBox.RenderSize));
                Assert.True(narrowTitleBounds.Bottom <= narrowValueBounds.Top);
            }
        });
    }

    private static void AssertGroupRowDoesNotOverlap(
        AppSettingsGroup group,
        ContentControl container,
        FrameworkElement value)
    {
        var row = Assert.IsType<AppSettingsRow>(container.Content);
        var title = Assert.IsType<WpfTextBlock>(
            row.Template!.FindName("TitlePresenter", row));
        Assert.True(row.ActualWidth > 0);
        Assert.True(row.ActualHeight > 0);
        Assert.True(group.ActualWidth > 0);
        Assert.False(row.IsNarrowLayout);

        var titleBounds = title.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), title.RenderSize));
        var valueBounds = value.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), value.RenderSize));
        Assert.True(
            titleBounds.Right <= valueBounds.Left,
            "The row title must not overlap the right-side control.");
    }

    private static void AssertControlDoesNotOverlapTitle(
        AppSettingsRow row,
        string title,
        FrameworkElement value)
    {
        var titleBlock = Assert.IsType<WpfTextBlock>(
            row.Template!.FindName("TitlePresenter", row));
        Assert.Equal(title, titleBlock.Text);
        Assert.True(row.ActualWidth > 0);
        Assert.True(row.ActualHeight > 0);

        var titleBounds = titleBlock.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), titleBlock.RenderSize));
        var valueBounds = value.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), value.RenderSize));
        Assert.True(titleBounds.Right <= valueBounds.Left);
        Assert.True(valueBounds.Right <= row.ActualWidth);
    }

    private static void AssertControlBelowTitle(
        AppSettingsRow row,
        string title,
        FrameworkElement value)
    {
        var titleBlock = Assert.IsType<WpfTextBlock>(
            row.Template!.FindName("TitlePresenter", row));
        Assert.Equal(title, titleBlock.Text);
        Assert.True(row.ActualWidth > 0);
        Assert.True(row.ActualHeight > 0);

        var titleBounds = titleBlock.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), titleBlock.RenderSize));
        var valueBounds = value.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), value.RenderSize));
        Assert.True(titleBounds.Bottom <= valueBounds.Top);
        Assert.True(valueBounds.Right <= row.ActualWidth);
    }

    private static bool HasGroupAncestor(DependencyObject element)
    {
        var current = LogicalTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is AppSettingsGroup)
            {
                return true;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static Style FindStyle(string key) =>
        (Style)global::System.Windows.Application.Current!.FindResource(key);

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
