using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using NovelSpeaker.App.Shell.Navigation;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SettingsPageViewTests
{
    [Fact]
    public void SettingsPage_uses_formal_header_groups_and_navigation_rows()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<SettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 800));

                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Same(page.FindResource(typeof(AppPageHeader)), header.Style);
                Assert.Equal("设置", header.Title);
                Assert.Null(header.BackCommand);

                var rootGrid = Assert.IsType<Grid>(page.Content);
                Assert.Equal(new Thickness(24), rootGrid.Margin);
                Assert.Equal(Brushes.Transparent, page.Background);

                var groups = VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(page).ToArray();
                Assert.Equal(3, groups.Length);
                Assert.Equal(["常用", "文本处理", "应用"], groups.Select(group => group.Header));
                Assert.Equal([3, 2, 3], groups.Select(group => group.Items.Count));
                Assert.All(groups, group =>
                {
                    Assert.Same(page.FindResource(typeof(AppSettingsGroup)), group.Style);
                    Assert.Equal(new Thickness(0, 0, 0, 16), group.Margin);
                });

                var expectedTitles = new[]
                {
                    "播放设置", "TTS 规则", "常规", "导入与文本", "章节规则", "缓存与数据", "外观", "诊断与关于"
                };
                var expectedIcons = new[]
                {
                    SymbolRegular.PlayCircle24,
                    SymbolRegular.Speaker124,
                    SymbolRegular.Settings24,
                    SymbolRegular.DocumentText24,
                    SymbolRegular.TextBulletListSquare24,
                    SymbolRegular.Database24,
                    SymbolRegular.DarkTheme24,
                    SymbolRegular.Info24
                };
                var rows = VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(page).ToArray();
                Assert.Equal(expectedTitles, rows.Select(row => row.Title));
                Assert.All(rows, row =>
                {
                    Assert.Same(page.FindResource(typeof(AppSettingsNavigationRow)), row.Style);
                    Assert.True(row.Focusable);
                    Assert.True(row.IsTabStop);
                    Assert.Equal(row.Title, AutomationProperties.GetName(row));
                    Assert.Equal(row.Title, row.ToolTip);
                    Assert.NotNull(row.Command);
                    Assert.True(row.Command!.CanExecute(null));
                });

                for (var index = 0; index < rows.Length; index++)
                {
                    var icons = VisualTreeTestHelper.FindDescendants<SymbolIcon>(rows[index]).ToArray();
                    Assert.Equal(2, icons.Length);
                    Assert.Equal(expectedIcons[index], icons[0].Symbol);
                    Assert.Equal(SymbolRegular.ChevronRight24, icons[1].Symbol);
                }

                rows[0].IsEnabled = false;
                var disabledIconBrush = Assert.IsType<SolidColorBrush>(
                    page.FindResource("App.Brush.Interaction.Foreground.Disabled"));
                Assert.All(
                    VisualTreeTestHelper.FindDescendants<SymbolIcon>(rows[0]),
                    icon => Assert.Same(disabledIconBrush, icon.Foreground));

                Assert.Empty(VisualTreeTestHelper.FindDescendants<TextBox>(page));
                Assert.Empty(VisualTreeTestHelper.FindDescendants<ComboBox>(page));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void SettingsPage_navigation_rows_do_not_overlap_at_narrow_window_and_supported_dpi()
    {
        foreach (var scale in new[] { 1d, 1.5d })
        {
            SettingsPage_navigation_rows_do_not_overlap_at_narrow_window_and_supported_dpi_for_scale(scale);
        }
    }

    private void SettingsPage_navigation_rows_do_not_overlap_at_narrow_window_and_supported_dpi_for_scale(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<SettingsPage>();
                page.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(520, 900));

                var rows = VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(page).ToArray();
                Assert.Equal(8, rows.Length);
                foreach (var row in rows)
                {
                    Assert.True(row.ActualWidth > 0);
                    Assert.True(row.ActualHeight >= 60);

                    var title = Assert.Single(
                        VisualTreeTestHelper.FindDescendants<TextBlock>(row),
                        textBlock => textBlock.Text == row.Title);
                    var chevron = Assert.Single(
                        VisualTreeTestHelper.FindDescendants<SymbolIcon>(row),
                        icon => icon.Symbol == SymbolRegular.ChevronRight24);
                    var titleBounds = title.TransformToAncestor(row)
                        .TransformBounds(new Rect(new Point(), title.RenderSize));
                    var chevronBounds = chevron.TransformToAncestor(row)
                        .TransformBounds(new Rect(new Point(), chevron.RenderSize));

                    Assert.True(
                        titleBounds.Right <= chevronBounds.Left,
                        $"{row.Title} overlaps its Chevron at {scale:0.##}x scale.");
                    Assert.True(titleBounds.Bottom <= row.ActualHeight);
                    Assert.True(chevronBounds.Right <= row.ActualWidth);
                }
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void SettingsPage_navigation_rows_activate_bound_routes_with_keyboard()
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigator = new RecordingNavigator();
            var page = new SettingsPage(new SettingsViewModel(navigator));
            using var host = WpfWindowHost.Show(new Window
            {
                Content = page,
                Width = 520,
                Height = 900,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var rows = VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(page).ToArray();
            var expectedRoutes = new[]
            {
                AppRoutes.PlaybackSettings,
                AppRoutes.TtsRules,
                AppRoutes.GeneralSettings,
                AppRoutes.ImportTextSettings,
                AppRoutes.ChapterRules,
                AppRoutes.CacheAndData,
                AppRoutes.AppearanceSettings,
                AppRoutes.DiagnosticsAbout
            };

            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                Assert.True(row.Focus());
                Assert.True(row.IsKeyboardFocused);

                var source = PresentationSource.FromVisual(row);
                Assert.NotNull(source);
                var keyDown = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    source!,
                    Environment.TickCount,
                    Key.Space)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                };
                row.RaiseEvent(keyDown);

                var keyUp = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    source!,
                    Environment.TickCount,
                    Key.Space)
                {
                    RoutedEvent = Keyboard.KeyUpEvent
                };
                row.RaiseEvent(keyUp);

                Assert.Same(expectedRoutes[index], navigator.LastRoute);
            }
        });
    }

    private sealed class RecordingNavigator : IAppNavigator
    {
        public AppRoute? LastRoute { get; private set; }

        public Task<bool> NavigateAsync(
            AppRoute route,
            CancellationToken cancellationToken,
            bool bypassGuard = false)
        {
            LastRoute = route;
            return Task.FromResult(true);
        }

        public AppRoute CurrentRoute => AppRoutes.Settings;

        public Task<bool> NavigateBackAsync(
            CancellationToken cancellationToken,
            bool bypassGuard = false) =>
            Task.FromResult(false);
    }
}
