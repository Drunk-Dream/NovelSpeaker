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
