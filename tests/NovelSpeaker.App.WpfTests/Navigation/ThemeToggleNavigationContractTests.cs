using System.Windows;
using System.Windows.Automation;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Settings;
using Wpf.Ui.Controls;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Navigation;

public sealed class ThemeToggleNavigationContractTests
{
    [Fact]
    public void Theme_toggle_is_the_last_footer_action_and_binds_to_shell_projection()
    {
        var document = XDocument.Load(Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shell",
            "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace ui = "http://schemas.lepo.co/wpfui/2022/xaml";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var footer = Assert.Single(document.Descendants(ui + "NavigationView.FooterMenuItems"));
        var footerItems = footer.Elements(ui + "NavigationViewItem").ToArray();
        var themeItem = Assert.IsType<XElement>(footerItems.Last());

        Assert.Equal("ThemeToggleNavigationItem", (string?)themeItem.Attribute(x + "Name"));
        Assert.Equal("{StaticResource App.Navigation.Entry}", (string?)themeItem.Attribute("Style"));
        Assert.Equal("{Binding ThemeToggleText}", (string?)themeItem.Attribute("Content"));
        Assert.Equal("{Binding ThemeToggleText}", (string?)themeItem.Attribute("ToolTip"));
        Assert.Equal("{Binding ThemeToggleText}", (string?)themeItem.Attribute("AutomationProperties.Name"));
        Assert.Equal("{Binding ToggleLightDarkThemeCommand}", (string?)themeItem.Attribute("Command"));

        var icon = Assert.Single(themeItem.Descendants(ui + "SymbolIcon"));
        var symbols = icon.Descendants(presentation + "Setter")
            .Where(setter => (string?)setter.Attribute("Property") == "Symbol")
            .Select(setter => (string?)setter.Attribute("Value"))
            .ToArray();
        Assert.Contains("WeatherMoon24", symbols);
        Assert.Contains("WeatherSunny24", symbols);
    }

    [Fact]
    public async Task Theme_toggle_footer_projects_target_theme_in_expanded_and_compact_navigation()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            var runtime = provider.GetRequiredService<IThemeRuntime>();
            runtime.ApplyLightTheme();
            var window = provider.GetRequiredService<MainWindow>();
            WpfWindowHost.Show(window);
            try
            {
                await WpfTestHost.DrainDispatcherAsync();
                window.UpdateLayout();

                var navigation = window.NavigationViewControl;
                var themeItem = Assert.IsType<NavigationViewItem>(window.FindName("ThemeToggleNavigationItem"));
                Assert.Same(themeItem, navigation.FooterMenuItems.Cast<NavigationViewItem>().Last());
                Assert.Equal("切换到深色模式", themeItem.Content);
                Assert.Equal("切换到深色模式", themeItem.ToolTip);
                Assert.Equal("切换到深色模式", AutomationProperties.GetName(themeItem));
                Assert.Equal(
                    SymbolRegular.WeatherMoon24,
                    Assert.IsType<SymbolIcon>(themeItem.Icon).Symbol);

                navigation.IsPaneOpen = false;
                window.UpdateLayout();
                Assert.Equal(SymbolRegular.WeatherMoon24, Assert.IsType<SymbolIcon>(themeItem.Icon).Symbol);

                var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
                await viewModel.ToggleLightDarkThemeCommand.ExecuteAsync(null);
                await WpfTestHost.DrainDispatcherAsync();
                window.UpdateLayout();

                Assert.Equal("切换到浅色模式", themeItem.Content);
                Assert.Equal("切换到浅色模式", themeItem.ToolTip);
                Assert.Equal("切换到浅色模式", AutomationProperties.GetName(themeItem));
                Assert.Equal(
                    SymbolRegular.WeatherSunny24,
                    Assert.IsType<SymbolIcon>(themeItem.Icon).Symbol);

                await provider
                    .GetRequiredService<IAppSettingsService>()
                    .UpdateAsync(new AppSettingsUpdate { Theme = "Light" }, CancellationToken.None);
                await WpfTestHost.DrainDispatcherAsync();
                window.UpdateLayout();

                Assert.Equal("切换到深色模式", themeItem.Content);
                Assert.Equal(SymbolRegular.WeatherMoon24, Assert.IsType<SymbolIcon>(themeItem.Icon).Symbol);
            }
            finally
            {
                runtime.ApplyLightTheme();
                window.Close();
                await WpfTestHost.DrainDispatcherAsync();
                await provider.DisposeAsync();
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
}
