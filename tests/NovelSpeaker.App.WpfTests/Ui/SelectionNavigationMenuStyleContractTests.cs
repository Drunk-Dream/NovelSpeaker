using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using NovelSpeaker.StyleGallery;
using Wpf.Ui.Controls;
using Xunit;
using WpfButton = System.Windows.Controls.Button;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SelectionNavigationMenuStyleContractTests
{
    private void Selection_navigation_menu_styles_resolve_through_the_provider_style_chains()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);

            AssertChainContains(application, "App.Navigation.Entry", "Provider.NavigationViewItem");
            AssertChainContains(application, "App.Navigation.SettingsEntry", "Provider.Button");
            AssertChainContains(application, "App.Menu.Surface", "Provider.Menu");
            AssertChainContains(application, "App.Menu.ContextSurface", "Provider.ContextMenu");
            AssertChainContains(application, "App.Menu.Item", "Provider.MenuItem");
            AssertChainContains(application, "App.Menu.DangerItem", "Provider.MenuItem");
            AssertChainContains(application, "App.Menu.GroupHeader", "Provider.MenuItem");
            AssertChainContains(application, "App.Menu.Separator", "Provider.Separator");

            foreach (var key in new[]
                     {
                         "App.Selection.ListItem",
                         "App.Selection.CardItem",
                         "App.Selection.CurrentItem",
                         "App.Selection.DropTarget",
                         "App.Selection.MultiSelectItem"
                     })
            {
                var style = Assert.IsType<Style>(application.FindResource(key));
                Assert.Equal(typeof(Border), style.TargetType);
                Assert.DoesNotContain(
                    EnumerateSetters(style),
                    setter => string.Equals(
                        setter.Property?.Name,
                        "Template",
                        StringComparison.Ordinal));
            }

            foreach (var key in new[]
                     {
                         "App.Selection.Content.Primary",
                         "App.Selection.Content.Secondary",
                         "App.Selection.Content.Title",
                         "App.Selection.Content.AccentSecondary",
                         "App.Selection.Content.RuleTitle",
                         "App.Selection.Content.RuleSecondary"
                     })
            {
                var style = Assert.IsType<Style>(application.FindResource(key));
                Assert.Equal(typeof(WpfTextBlock), style.TargetType);
                Assert.DoesNotContain(
                    EnumerateSetters(style),
                    setter => string.Equals(
                        setter.Property?.Name,
                        "Template",
                        StringComparison.Ordinal));
            }
        });
    }

    private void Menu_styles_define_single_state_owners_and_independent_separators()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var menus = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Menus.xaml"));
        var item = menus.Root!.Elements(presentation + "Style")
            .Single(style => (string?)style.Attribute(xaml + "Key") == "App.Menu.Item");
        var itemTriggers = item.Element(presentation + "Style.Triggers")!.Elements().ToArray();

        Assert.Contains(itemTriggers, trigger =>
            trigger.Name.LocalName == "Trigger" &&
            (string?)trigger.Attribute("Property") == "IsHighlighted" &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Surface.Hover}"));
        Assert.Contains(itemTriggers, trigger =>
            trigger.Name.LocalName == "Trigger" &&
            (string?)trigger.Attribute("Property") == "IsPressed" &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Surface.Pressed}"));
        Assert.Contains(itemTriggers, trigger =>
            trigger.Name.LocalName == "Trigger" &&
            (string?)trigger.Attribute("Property") == "IsChecked" &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Accent.Subtle}"));
        Assert.Contains(itemTriggers, trigger =>
            trigger.Name.LocalName == "MultiTrigger" &&
            trigger.Element(presentation + "MultiTrigger.Conditions")?.Elements(presentation + "Condition")
                .Any(condition =>
                    (string?)condition.Attribute("Property") == "IsChecked" &&
                    (string?)condition.Attribute("Value") == "True") == true &&
            trigger.Element(presentation + "MultiTrigger.Conditions")?.Elements(presentation + "Condition")
                .Any(condition =>
                    (string?)condition.Attribute("Property") == "IsPressed" &&
                    (string?)condition.Attribute("Value") == "True") == true &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Surface.Pressed}"));
        Assert.Contains(itemTriggers, trigger =>
            trigger.Name.LocalName == "MultiTrigger" &&
            trigger.Element(presentation + "MultiTrigger.Conditions")?.Elements(presentation + "Condition")
                .Count(condition => (string?)condition.Attribute("Value") == "True") == 3 &&
            trigger.Element(presentation + "MultiTrigger.Conditions")?.Elements(presentation + "Condition")
                .Any(condition => (string?)condition.Attribute("Property") == "IsChecked") == true &&
            trigger.Element(presentation + "MultiTrigger.Conditions")?.Elements(presentation + "Condition")
                .Any(condition => (string?)condition.Attribute("Property") == "IsPressed") == true &&
            trigger.Element(presentation + "MultiTrigger.Conditions")?.Elements(presentation + "Condition")
                .Any(condition => (string?)condition.Attribute("Property") == "IsHighlighted") == true &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Surface.Pressed}"));
        Assert.Contains(itemTriggers, trigger =>
            trigger.Name.LocalName == "MultiTrigger" &&
            trigger.Element(presentation + "MultiTrigger.Conditions")?.Elements(presentation + "Condition")
                .Any(condition =>
                    (string?)condition.Attribute("Property") == "IsChecked" &&
                    (string?)condition.Attribute("Value") == "True") == true &&
            trigger.Element(presentation + "MultiTrigger.Conditions")?.Elements(presentation + "Condition")
                .Any(condition =>
                    (string?)condition.Attribute("Property") == "IsHighlighted" &&
                    (string?)condition.Attribute("Value") == "True") == true &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Accent.Subtle.Hover}"));
        Assert.Contains(itemTriggers, trigger =>
            trigger.Name.LocalName == "Trigger" &&
            (string?)trigger.Attribute("Property") == "IsEnabled" &&
            (string?)trigger.Attribute("Value") == "False" &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Foreground" &&
                (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Foreground.Disabled}"));
        Assert.DoesNotContain(
            item.Elements(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "BorderBrush" ||
                       (string?)setter.Attribute("Property") == "BorderThickness");

        var danger = menus.Root.Elements(presentation + "Style")
            .Single(style => (string?)style.Attribute(xaml + "Key") == "App.Menu.DangerItem");
        Assert.Contains(
            danger.Descendants(presentation + "Trigger"),
            trigger => (string?)trigger.Attribute("Property") == "IsHighlighted" &&
                       trigger.Elements(presentation + "Setter").Any(setter =>
                           (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Danger.Subtle}"));
        Assert.Contains(
            danger.Descendants(presentation + "Trigger"),
            trigger => (string?)trigger.Attribute("Property") == "IsEnabled" &&
                       (string?)trigger.Attribute("Value") == "False" &&
                       trigger.Elements(presentation + "Setter").Any(setter =>
                           (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Foreground.Disabled}"));

        var separator = menus.Root.Elements(presentation + "Style")
            .Single(style => (string?)style.Attribute(xaml + "Key") == "App.Menu.Separator");
        Assert.Equal("{x:Type Separator}", separator.Attribute("TargetType")?.Value);
        Assert.Equal("{StaticResource Provider.Separator}", separator.Attribute("BasedOn")?.Value);
        Assert.Contains(
            separator.Elements(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Margin" &&
                      (string?)setter.Attribute("Value") == "12,4");
        Assert.Contains(
            separator.Elements(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Opacity" &&
                      (string?)setter.Attribute("Value") == "1");
        Assert.Contains(
            separator.Elements(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Background" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Border.Subtle}");

        var bookCard = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Features",
            "Library",
            "BookCardView.xaml"));
        Assert.Contains(
            bookCard.Descendants(presentation + "Separator"),
            element => (string?)element.Attribute("Style") == "{StaticResource App.Menu.Separator}");

        var rules = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "ControlThemes",
            "Rules.xaml"));
        Assert.DoesNotContain(
            rules.Descendants(presentation + "Style"),
            style => (string?)style.Attribute("TargetType") == "Separator" &&
                     style.Attribute("BasedOn") is null);
        Assert.All(
            rules.Descendants(presentation + "Separator"),
            element => Assert.Contains(
                element.Descendants(presentation + "Style"),
                style => (string?)style.Attribute("BasedOn") == "{StaticResource App.Menu.Separator}"));
    }

    private void Selection_content_styles_project_persistent_and_disabled_text_states()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var application = global::System.Windows.Application.Current!;
            var selected = new SelectionTextFixture { IsCurrent = true };
            var accentRest = new WpfTextBlock
            {
                Style = Assert.IsType<Style>(application.FindResource("App.Selection.Content.AccentSecondary")),
                DataContext = new SelectionTextFixture()
            };
            var accentSelected = new WpfTextBlock
            {
                Style = Assert.IsType<Style>(application.FindResource("App.Selection.Content.AccentSecondary")),
                DataContext = selected
            };
            var titleSelected = new WpfTextBlock
            {
                Style = Assert.IsType<Style>(application.FindResource("App.Selection.Content.Title")),
                DataContext = selected
            };
            var disabled = new WpfTextBlock
            {
                Style = Assert.IsType<Style>(application.FindResource("App.Selection.Content.Primary")),
                DataContext = selected,
                IsEnabled = false
            };
            var window = new Window
            {
                Content = new StackPanel
                {
                    Children = { accentRest, accentSelected, titleSelected, disabled }
                },
                Width = 400,
                Height = 200,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                Assert.Equal(BrushColor("App.Brush.Accent.Default"), ColorOf(accentRest.Foreground));
                Assert.Equal(
                    BrushColor("App.Brush.Interaction.Foreground.Selected"),
                    ColorOf(accentSelected.Foreground));
                Assert.Equal(
                    BrushColor("App.Brush.Interaction.Foreground.Selected"),
                    ColorOf(titleSelected.Foreground));
                Assert.Equal(
                    BrushColor("App.Brush.Interaction.Foreground.Disabled"),
                    ColorOf(disabled.Foreground));
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    private void Selection_content_callers_do_not_redeclare_semantic_foreground_triggers()
    {
        var repositoryRoot = LocateRepositoryRoot();
        foreach (var relativePath in new[]
                 {
                     Path.Combine("src", "NovelSpeaker.App", "Features", "BookDetails", "BookDetailsPage.xaml"),
                     Path.Combine("src", "NovelSpeaker.App", "Features", "Cache", "CacheManagementPage.xaml"),
                     Path.Combine("src", "NovelSpeaker.App", "Features", "Playback", "Components", "PlayerView.xaml"),
                     Path.Combine("src", "NovelSpeaker.App", "Shell", "MainWindow.xaml"),
                     Path.Combine("src", "NovelSpeaker.App", "Shared", "Theming", "Resources", "ControlThemes", "Rules.xaml")
                 })
        {
            var document = XDocument.Load(Path.Combine(repositoryRoot, relativePath));
            Assert.DoesNotContain(
                document.Descendants().Where(element => element.Name.LocalName == "Setter"),
                setter => (string?)setter.Attribute("Value") is
                    "{DynamicResource App.Brush.Interaction.Foreground.Selected}" or
                    "{DynamicResource App.Brush.Interaction.Foreground.Disabled}");
        }
    }

    private void Selection_persistent_states_keep_their_accent_hover_priority()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Selection.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var stateProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.Selection.ListItem"] = "IsSelected",
            ["App.Selection.CurrentItem"] = "IsCurrent",
            ["App.Selection.DropTarget"] = "IsDropTarget",
            ["App.Selection.MultiSelectItem"] = "IsSelectedForActiveCache"
        };

        foreach (var (styleKey, stateProperty) in stateProperties)
        {
            var style = document.Root!.Elements().Single(element =>
                (string?)element.Attribute(xaml + "Key") == styleKey);
            var selectedHover = style.Descendants().Single(trigger =>
                trigger.Name.LocalName == "MultiDataTrigger" &&
                trigger.Elements().Where(element => element.Name.LocalName == "MultiDataTrigger.Conditions")
                    .Elements()
                    .Any(condition =>
                        (string?)condition.Attribute("Binding") == $"{{Binding {stateProperty}}}" &&
                        (string?)condition.Attribute("Value") == "True") &&
                trigger.Elements().Where(element => element.Name.LocalName == "MultiDataTrigger.Conditions")
                    .Elements()
                    .Any(condition =>
                        (string?)condition.Attribute("Value") == "True" &&
                        ((string?)condition.Attribute("Binding"))?.Contains("IsMouseOver", StringComparison.Ordinal) == true));
            Assert.Contains(
                selectedHover.Elements(),
                setter => (string?)setter.Attribute("Property") == "Background" &&
                          (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Accent.Subtle.Hover}");
            Assert.Contains(
                selectedHover.Elements(),
                setter => (string?)setter.Attribute("Property") == "TextElement.Foreground" &&
                          (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Interaction.Foreground.Selected}");
        }
    }

    private void Selection_gallery_covers_all_variants_states_and_accessibility_metadata()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("selection");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var rows = FindMatrixRows(scene).ToArray();
                var matrix = FindDescendants<Grid>(scene).Single(grid =>
                    AutomationProperties.GetAutomationId(grid) == "selection-state-matrix-grid");
                Assert.Equal(SelectionPreviewStates.Length + 1, matrix.ColumnDefinitions.Count);
                Assert.All(
                    matrix.ColumnDefinitions.Skip(1),
                    column => Assert.True(column.ActualWidth > 0));
                Assert.All(rows, row =>
                {
                    Assert.True(row.ActualWidth > 0);
                    Assert.True(row.ActualHeight > 0);
                });
                var expectedIds = SelectionStyleVariants
                    .SelectMany(variant => SelectionPreviewStates.Select(state =>
                        $"selection-{variant}-{state}"))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(
                    expectedIds,
                    rows.Select(row => AutomationProperties.GetAutomationId(row)).Order(StringComparer.Ordinal));
                Assert.All(rows, row =>
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(row))));

                Assert.Equal(
                    Colors.Transparent,
                    ColorOf(GetRow(rows, "selection-listitem-default").Background));
                Assert.Equal(
                    BrushColor("App.Brush.Accent.Subtle"),
                    ColorOf(GetRow(rows, "selection-listitem-selected").Background));
                Assert.Equal(
                    BrushColor("App.Brush.Interaction.Surface.Hover"),
                    ColorOf(GetRow(rows, "selection-listitem-hover").Background));
                Assert.Equal(
                    BrushColor("App.Brush.Focus"),
                    ColorOf(GetRow(rows, "selection-listitem-focus").BorderBrush));
                Assert.Equal(
                    BrushColor("App.Brush.Accent.Subtle"),
                    ColorOf(GetRow(rows, "selection-currentitem-current").Background));
                Assert.Equal(
                    BrushColor("App.Brush.Focus"),
                    ColorOf(GetRow(rows, "selection-droptarget-droptarget").BorderBrush));
                Assert.Equal(
                    BrushColor("App.Brush.Accent.Subtle"),
                    ColorOf(GetRow(rows, "selection-multiselectitem-multiselect").Background));
                Assert.Equal(
                    BrushColor("App.Brush.Surface.Secondary"),
                    ColorOf(GetRow(rows, "selection-carditem-disabled").Background));
                Assert.Equal(0.5, GetRow(rows, "selection-carditem-disabled").Opacity, 2);

                var list = FindDescendants<ItemsControl>(scene).Single(control =>
                    AutomationProperties.GetAutomationId(control) == "selection-virtualized-host");
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
                Assert.Contains(
                    FindDescendants<Border>(scene),
                    row => AutomationProperties.GetAutomationId(row) == "selection-virtualized-row-03");
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    private void Selection_virtualized_row_text_uses_theme_semantic_brushes()
    {
        foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
        {
            Selection_virtualized_row_text_uses_theme_semantic_brushes_for_theme(theme);
        }
    }

    private void Selection_virtualized_row_text_uses_theme_semantic_brushes_for_theme(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("selection");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var row = GetRow(FindVirtualizedRows(scene), "selection-virtualized-row-01");
                var textBlocks = FindDescendants<WpfTextBlock>(row).ToArray();

                Assert.Equal(2, textBlocks.Length);
                Assert.Equal(
                    BrushColor("App.Brush.Text.Primary"),
                    ColorOf(textBlocks[0].Foreground));
                Assert.Equal(
                    BrushColor("App.Brush.Text.Secondary"),
                    ColorOf(textBlocks[1].Foreground));
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    private void Selection_facts_follow_data_through_virtualized_container_recycling()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("selection");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var list = FindDescendants<ItemsControl>(scene).Single(control =>
                    AutomationProperties.GetAutomationId(control) == "selection-virtualized-host");
                var viewport = Assert.Single(FindDescendants<ScrollViewer>(list));
                Assert.True(ScrollViewer.GetCanContentScroll(list));
                Assert.True(ScrollViewer.GetCanContentScroll(viewport));
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                var topSelected = GetRow(FindSelectionRows(scene), "selection-virtualized-row-03");
                Assert.Equal(
                    BrushColor("App.Brush.Accent.Subtle"),
                    ColorOf(topSelected.Background));
                Assert.All(
                    FindDescendants<WpfTextBlock>(topSelected),
                    text => Assert.Equal(
                        BrushColor("App.Brush.Interaction.Foreground.Selected"),
                        ColorOf(text.Foreground)));

                var disabled = GetRow(FindMatrixRows(scene), "selection-listitem-disabled");
                Assert.All(
                    FindDescendants<WpfTextBlock>(disabled),
                    text => Assert.Equal(
                        BrushColor("App.Brush.Interaction.Foreground.Disabled"),
                        ColorOf(text.Foreground)));

                viewport.ScrollToEnd();
                PumpDispatcher();
                Assert.True(viewport.ScrollableHeight > 0);
                Assert.True(viewport.VerticalOffset > 0);
                Assert.DoesNotContain(
                    "selection-virtualized-row-03",
                    FindVirtualizedRows(scene)
                        .Select(row => AutomationProperties.GetAutomationId(row)));

                viewport.ScrollToTop();
                PumpDispatcher();
                var recycled = GetRow(FindSelectionRows(scene), "selection-virtualized-row-03");
                Assert.Equal(
                    BrushColor("App.Brush.Accent.Subtle"),
                    ColorOf(recycled.Background));
                Assert.Equal(
                    BrushColor("App.Brush.Accent.Default"),
                    ColorOf(recycled.BorderBrush));

                foreach (var row in FindSelectionRows(scene))
                {
                    var id = AutomationProperties.GetAutomationId(row);
                    if (!id.StartsWith("selection-virtualized-row-", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (id is "selection-virtualized-row-03" or "selection-virtualized-row-08")
                    {
                        Assert.Equal(
                            BrushColor("App.Brush.Accent.Subtle"),
                            ColorOf(row.Background));
                    }
                    else
                    {
                        Assert.Equal(Colors.Transparent, ColorOf(row.Background));
                    }
                }
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    private void Multi_select_item_style_reflects_the_active_cache_selection_fact()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var application = global::System.Windows.Application.Current!;
            var style = Assert.IsType<Style>(application.FindResource("App.Selection.MultiSelectItem"));
            var selected = new Border
            {
                Style = style,
                DataContext = new ActiveCacheSelectionFixture(isSelectedForActiveCache: true)
            };
            var unselected = new Border
            {
                Style = style,
                DataContext = new ActiveCacheSelectionFixture(isSelectedForActiveCache: false)
            };
            var window = new Window
            {
                Content = new StackPanel { Children = { selected, unselected } },
                Width = 400,
                Height = 200,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                Assert.Equal(BrushColor("App.Brush.Accent.Subtle"), ColorOf(selected.Background));
                Assert.Equal(BrushColor("App.Brush.Accent.Default"), ColorOf(selected.BorderBrush));
                Assert.Equal(Colors.Transparent, ColorOf(unselected.Background));
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    private void Navigation_gallery_covers_primary_entries_settings_rows_and_keyboard_focus()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("navigation");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var navigation = FindDescendants<NavigationView>(scene).Single();
                Assert.True(navigation.IsPaneOpen);
                var items = navigation.MenuItems.OfType<NavigationViewItem>().ToArray();
                Assert.Equal(4, items.Length);
                Assert.All(items, item =>
                {
                    Assert.Same(
                        global::System.Windows.Application.Current!.FindResource("Provider.NavigationViewItem"),
                        item.Style?.BasedOn);
                    Assert.NotNull(item.Template);
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)));
                    Assert.True(item.ActualWidth > 0);
                    Assert.True(item.ActualHeight > 0);
                });
                Assert.Single(items, item => item.IsActive);
                Assert.Contains(items, item => !item.IsEnabled && item.Opacity < 1);
                Assert.True(items[1].Focus());
                Assert.True(items[1].IsKeyboardFocusWithin);

                var entries = FindDescendants<WpfButton>(scene)
                    .Where(button => AutomationProperties.GetAutomationId(button)?.StartsWith(
                        "settings-entry-",
                        StringComparison.Ordinal) == true)
                    .ToArray();
                Assert.Equal(6, entries.Length);
                Assert.All(entries, entry =>
                {
                    AssertChainContains(
                        global::System.Windows.Application.Current!,
                        entry.Style!,
                        "Provider.Button");
                    Assert.Equal(2, FindDescendants<SymbolIcon>(entry).Count(icon =>
                        icon.ActualWidth > 0 && icon.ActualHeight > 0));
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(entry)));
                });

                var hoverEntry = GetEntry(entries, "settings-entry-playback");
                Assert.Equal(
                    BrushColor("App.Brush.Interaction.Surface.Hover"),
                    ColorOf(hoverEntry.Background));
                var focusEntry = GetEntry(entries, "settings-entry-appearance");
                Assert.Equal(
                    BrushColor("App.Brush.Focus"),
                    ColorOf(focusEntry.BorderBrush));
                Assert.False(GetEntry(entries, "settings-entry-disabled").IsEnabled);
                Assert.True(GetEntry(entries, "settings-entry-general").Focus());
                Assert.True(GetEntry(entries, "settings-entry-general").IsKeyboardFocusWithin);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    private void Menus_gallery_covers_surfaces_danger_grouping_and_neutral_close()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("menus");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });

            try
            {
                host.Window.UpdateLayout();
                var application = global::System.Windows.Application.Current!;
                var lightSeparatorColor = PaletteColor(GalleryTheme.Light, "App.Brush.Border.Subtle");
                var darkSeparatorColor = PaletteColor(GalleryTheme.Dark, "App.Brush.Border.Subtle");
                var menu = FindDescendants<Menu>(scene).Single();
                Assert.Same(application.FindResource("App.Menu.Surface"), menu.Style);
                Assert.Same(application.FindResource("App.Elevation.Medium"), menu.Effect);
                Assert.Same(
                    application.FindResource("App.Brush.Surface.Raised"),
                    menu.Background);

                var inlineItems = menu.Items.OfType<WpfMenuItem>().ToArray();
                Assert.Equal(10, inlineItems.Length);
                Assert.Equal("书籍操作", inlineItems[0].Header);
                Assert.False(inlineItems[0].IsEnabled);
                Assert.Equal("打开详情", inlineItems[1].Header);
                Assert.Equal("Hover 预览", inlineItems[2].Header);
                Assert.Equal("Pressed 预览", inlineItems[3].Header);
                Assert.True(inlineItems[3].IsPressed);
                Assert.Equal("Checked", inlineItems[4].Header);
                Assert.True(inlineItems[4].IsChecked);
                Assert.Equal("Checked + Hover", inlineItems[5].Header);
                Assert.True(inlineItems[5].IsChecked);
                Assert.Equal("Checked + Pressed", inlineItems[6].Header);
                Assert.True(inlineItems[6].IsChecked);
                Assert.True(inlineItems[6].IsPressed);
                Assert.Equal("Disabled", inlineItems[7].Header);
                Assert.False(inlineItems[7].IsEnabled);
                Assert.Equal("Danger", inlineItems[8].Tag);
                Assert.Equal("删除书籍", inlineItems[8].Header);
                Assert.Equal("Close", inlineItems[9].Header);
                Assert.NotSame(inlineItems[8].Style, inlineItems[9].Style);

                var separatorStyle = application.FindResource("App.Menu.Separator");
                var separators = menu.Items.OfType<Separator>().ToArray();
                Assert.Equal(2, separators.Length);
                Assert.All(separators, separator =>
                {
                    Assert.Same(separatorStyle, separator.Style);
                    Assert.Equal(new Thickness(12, 4, 12, 4), separator.Margin);
                    Assert.Equal(1, separator.Opacity);
                    Assert.False(separator.IsHitTestVisible);
                    Assert.False(separator.Focusable);
                });
                Assert.Same(
                    application.FindResource("App.Brush.Interaction.Surface.Hover"),
                    inlineItems[2].Background);
                Assert.Same(
                    application.FindResource("App.Brush.Interaction.Surface.Pressed"),
                    inlineItems[3].Background);
                Assert.Same(
                    application.FindResource("App.Brush.Accent.Subtle"),
                    inlineItems[4].Background);
                Assert.Same(
                    application.FindResource("App.Brush.Accent.Subtle.Hover"),
                    inlineItems[5].Background);
                Assert.Same(
                    application.FindResource("App.Brush.Interaction.Surface.Pressed"),
                    inlineItems[6].Background);
                Assert.Same(
                    application.FindResource("App.Brush.Interaction.Foreground.Disabled"),
                    inlineItems[7].Foreground);
                Assert.Same(
                    application.FindResource("App.Brush.Danger.Subtle"),
                    inlineItems[8].Background);

                var disabledDanger = new WpfMenuItem
                {
                    Header = "Disabled Danger",
                    IsEnabled = false,
                    Style = Assert.IsType<Style>(application.FindResource("App.Menu.DangerItem"))
                };
                SetHighlightedValue(disabledDanger, true);
                menu.Items.Add(disabledDanger);
                host.Window.UpdateLayout();
                Assert.Same(
                    application.FindResource("App.Brush.Interaction.Foreground.Disabled"),
                    disabledDanger.Foreground);
                Assert.Equal(OpacityOf("App.Opacity.Disabled"), disabledDanger.Opacity);

                var previewSeparators = FindDescendants<Separator>(scene)
                    .Where(separator => AutomationProperties.GetAutomationId(separator) == "menus-preview-separator")
                    .ToArray();
                Assert.Equal(2, previewSeparators.Length);
                Assert.All(previewSeparators, separator =>
                {
                    separator.ApplyTemplate();
                    separator.UpdateLayout();
                    Assert.True(separator.ActualWidth >= 200d);
                    Assert.InRange(separator.ActualHeight, 0.75d, 1.1d);
                    AssertSeparatorLine(separator, lightSeparatorColor);
                });
                GalleryThemeRuntime.Apply(GalleryTheme.Dark);
                host.Window.UpdateLayout();
                Assert.All(
                    previewSeparators,
                    separator => AssertSeparatorLine(separator, darkSeparatorColor));
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                host.Window.UpdateLayout();
                Assert.All(
                    previewSeparators,
                    separator => AssertSeparatorLine(separator, lightSeparatorColor));

                var anchor = FindDescendants<WpfButton>(scene).Single(button =>
                    AutomationProperties.GetAutomationId(button) == "menus-context-anchor");
                var contextMenu = anchor.ContextMenu;
                Assert.NotNull(contextMenu);
                Assert.Same(application.FindResource("App.Menu.ContextSurface"), contextMenu.Style);
                Assert.Same(application.FindResource("App.Elevation.Medium"), contextMenu.Effect);
                Assert.Same(
                    application.FindResource("App.Brush.Surface.Raised"),
                    contextMenu.Background);
                Assert.Equal(
                    inlineItems.Select(item => item.Header),
                    contextMenu.Items.OfType<WpfMenuItem>().Select(item => item.Header));
                Assert.All(
                    contextMenu.Items.OfType<Separator>(),
                    separator => Assert.Same(separatorStyle, separator.Style));

                contextMenu.PlacementTarget = anchor;
                contextMenu.IsOpen = true;
                contextMenu.ApplyTemplate();
                contextMenu.UpdateLayout();
                var contextSeparators = contextMenu.Items.OfType<Separator>().ToArray();
                Assert.Equal(2, contextSeparators.Length);
                Assert.InRange(
                    contextSeparators[0].ActualWidth,
                    1d,
                    double.MaxValue);
                Assert.Equal(contextSeparators[0].ActualWidth, contextSeparators[1].ActualWidth);
                Assert.All(contextSeparators, separator =>
                {
                    Assert.InRange(separator.ActualHeight, 0.75d, 1.1d);
                    Assert.Equal(1, separator.Opacity);
                    Assert.Contains(
                        FindDescendants<Border>(separator),
                        line => line.ActualWidth > 0d &&
                                line.ActualHeight >= 0.75d &&
                                line.Background is SolidColorBrush brush &&
                                brush.Color == lightSeparatorColor &&
                                brush.Opacity >= 0.99d &&
                                line.Opacity >= 0.99d);
                });
                contextMenu.IsOpen = false;
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
            }
        });
    }

    private static void AssertChainContains(
        global::System.Windows.Application application,
        string styleKey,
        string providerKey)
    {
        var style = Assert.IsType<Style>(application.FindResource(styleKey));
        AssertChainContains(application, style, providerKey);
    }

    private static void AssertChainContains(
        global::System.Windows.Application application,
        Style style,
        string providerKey)
    {
        var chain = new List<Style>();
        for (var current = style; current is not null; current = current.BasedOn)
        {
            Assert.True(chain.All(candidate => !ReferenceEquals(candidate, current)),
                $"Style inheritance cycle detected at '{providerKey}'.");
            chain.Add(current);
        }

        Assert.Contains(
            chain,
            candidate => ReferenceEquals(candidate, application.FindResource(providerKey)));
    }

    private static IEnumerable<Setter> EnumerateSetters(Style style)
    {
        for (var current = style; current is not null; current = current.BasedOn)
        {
            foreach (var setter in current.Setters.OfType<Setter>())
            {
                yield return setter;
            }
        }
    }

    private static Border GetRow(IEnumerable<Border> rows, string automationId) =>
        Assert.IsType<Border>(rows.Single(row =>
            AutomationProperties.GetAutomationId(row) == automationId));

    private static WpfButton GetEntry(IEnumerable<WpfButton> entries, string automationId) =>
        Assert.IsType<WpfButton>(entries.Single(entry =>
            AutomationProperties.GetAutomationId(entry) == automationId));

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IReadOnlyList<Border> FindSelectionRows(DependencyObject root) =>
        FindVirtualizedRows(root);

    private static IReadOnlyList<Border> FindMatrixRows(DependencyObject root) =>
        FindDescendants<Border>(root)
            .Where(border => SelectionStyleVariants.Any(variant =>
                AutomationProperties.GetAutomationId(border)?.StartsWith(
                    $"selection-{variant}-",
                    StringComparison.Ordinal) == true))
            .ToArray();

    private static IReadOnlyList<Border> FindVirtualizedRows(DependencyObject root) =>
        FindDescendants<Border>(root)
            .Where(border => AutomationProperties.GetAutomationId(border)?.StartsWith(
                "selection-virtualized-row-",
                StringComparison.Ordinal) == true)
            .ToArray();

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

    [Fact]
    public void Selection_style_contracts_cover_provider_chains_and_gallery_states()
    {
        Selection_navigation_menu_styles_resolve_through_the_provider_style_chains();
        Menu_styles_define_single_state_owners_and_independent_separators();
        Selection_persistent_states_keep_their_accent_hover_priority();
        Selection_content_styles_project_persistent_and_disabled_text_states();
        Selection_content_callers_do_not_redeclare_semantic_foreground_triggers();
        Selection_gallery_covers_all_variants_states_and_accessibility_metadata();
    }

    [Fact]
    public void Selection_data_contracts_cover_virtualized_rows_and_active_cache_state()
    {
        Selection_virtualized_row_text_uses_theme_semantic_brushes();
        Selection_facts_follow_data_through_virtualized_container_recycling();
        Multi_select_item_style_reflects_the_active_cache_selection_fact();
    }

    [Fact]
    public void Navigation_menu_gallery_contracts_cover_entries_surfaces_and_focus()
    {
        Navigation_gallery_covers_primary_entries_settings_rows_and_keyboard_focus();
        Menus_gallery_covers_surfaces_danger_grouping_and_neutral_close();
    }

    private static Color BrushColor(string key) =>
        Assert.IsType<SolidColorBrush>(
            global::System.Windows.Application.Current!.FindResource(key)).Color;

    private static Color PaletteColor(GalleryTheme theme, string key)
    {
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var palette = XDocument.Load(Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Palettes",
            $"Palette.{theme}.xaml"));
        var colorText = palette.Root!
            .Elements(presentation + "SolidColorBrush")
            .Single(element => (string?)element.Attribute(xaml + "Key") == key)
            .Attribute("Color")?.Value
            ?? throw new InvalidOperationException($"Palette color '{key}' is missing.");
        return Assert.IsType<Color>(new ColorConverter().ConvertFromInvariantString(colorText));
    }

    private static Color? ColorOf(object? brush) =>
        brush is SolidColorBrush solid ? solid.Color : null;

    private static void AssertSeparatorLine(Separator separator, Color expectedColor)
    {
        Assert.Contains(
            FindDescendants<Border>(separator),
            line => line.ActualWidth > 0d &&
                    line.ActualHeight >= 0.75d &&
                    line.Background is SolidColorBrush brush &&
                    brush.Color == expectedColor &&
                    brush.Color.A > 0 &&
                    brush.Opacity >= 0.99d &&
                    line.Opacity >= 0.99d);
    }

    private static double OpacityOf(string key) =>
        Convert.ToDouble(global::System.Windows.Application.Current!.FindResource(key),
            global::System.Globalization.CultureInfo.InvariantCulture);

    private static void SetHighlightedValue(WpfMenuItem item, bool value)
    {
        var property = typeof(WpfMenuItem).GetProperty(
            nameof(WpfMenuItem.IsHighlighted),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var setter = property?.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("WPF MenuItem.IsHighlighted setter is unavailable.");
        setter.Invoke(item, [value]);
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

    private static readonly string[] SelectionStyleVariants =
    [
        "listitem",
        "carditem",
        "currentitem",
        "droptarget",
        "multiselectitem"
    ];

    private static readonly string[] SelectionPreviewStates =
    [
        "default",
        "hover",
        "selected",
        "current",
        "droptarget",
        "multiselect",
        "focus",
        "disabled"
    ];

    private sealed class ActiveCacheSelectionFixture
    {
        public ActiveCacheSelectionFixture(bool isSelectedForActiveCache)
        {
            IsSelectedForActiveCache = isSelectedForActiveCache;
        }

        public bool IsSelectedForActiveCache { get; }
    }

    private sealed class SelectionTextFixture
    {
        public bool IsSelected { get; init; }

        public bool IsCurrent { get; init; }

        public bool IsSelectedForActiveCache { get; init; }

        public bool IsDropTarget { get; init; }
    }
}
