using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
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
    [Fact]
    public void Selection_navigation_menu_styles_resolve_through_the_provider_style_chains()
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
        });
    }

    [Fact]
    public void Selection_gallery_covers_all_variants_states_and_accessibility_metadata()
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
                    BrushColor("App.Brush.Surface.Secondary"),
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

    [Fact]
    public void Selection_virtualized_row_text_uses_theme_semantic_brushes()
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

    [Fact]
    public void Selection_facts_follow_data_through_virtualized_container_recycling()
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

    [Fact]
    public void Multi_select_item_style_reflects_the_active_cache_selection_fact()
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

    [Fact]
    public void Navigation_gallery_covers_primary_entries_settings_rows_and_keyboard_focus()
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
                    BrushColor("App.Brush.Surface.Secondary"),
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

    [Fact]
    public void Menus_gallery_covers_surfaces_danger_grouping_and_neutral_close()
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
                var menu = FindDescendants<Menu>(scene).Single();
                Assert.Same(application.FindResource("App.Menu.Surface"), menu.Style);
                Assert.Same(application.FindResource("App.Elevation.Medium"), menu.Effect);
                Assert.Same(
                    application.FindResource("App.Brush.Surface.Raised"),
                    menu.Background);

                var inlineItems = menu.Items.OfType<WpfMenuItem>().ToArray();
                Assert.Equal(4, inlineItems.Length);
                Assert.Equal("书籍操作", inlineItems[0].Header);
                Assert.False(inlineItems[0].IsEnabled);
                Assert.Equal("打开详情", inlineItems[1].Header);
                Assert.Equal("Danger", inlineItems[2].Tag);
                Assert.Equal("删除书籍", inlineItems[2].Header);
                Assert.Equal("Close", inlineItems[3].Header);
                Assert.NotSame(inlineItems[2].Style, inlineItems[3].Style);
                Assert.Equal(2, menu.Items.OfType<Separator>().Count());

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

    private static Color BrushColor(string key) =>
        Assert.IsType<SolidColorBrush>(
            global::System.Windows.Application.Current!.FindResource(key)).Color;

    private static Color? ColorOf(object? brush) =>
        brush is SolidColorBrush solid ? solid.Color : null;

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
}
