using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Xml.Linq;
using NovelSpeaker.App.Shared.Presentation.Behaviors;
using NovelSpeaker.App.Features.Playback.Components;
using NovelSpeaker.StyleGallery;
using Xunit;
using Flyout = Wpf.Ui.Controls.Flyout;
using WpfUiButton = Wpf.Ui.Controls.Button;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class PlayerStopTimerEntryTests
{
    [Fact]
    public void Player_toolbar_exposes_accessible_stop_timer_flyout()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new PlayerView();
            var button = Assert.IsType<WpfUiButton>(view.FindName("StopTimerToolButton"));
            var speedButton = Assert.IsType<WpfUiButton>(view.FindName("SpeedMenuButton"));
            var flyout = Assert.IsType<Flyout>(view.FindName("StopTimerFlyout"));
            var customMinutes = Assert.IsType<TextBox>(view.FindName("CustomStopMinutesTextBox"));
            var cancelButton = Assert.IsType<Button>(view.FindName("CancelStopTimerButton"));
            var applyButton = Assert.IsType<Button>(view.FindName("ApplyCustomStopTimerButton"));

            Assert.Equal("定时停止", button.ToolTip);
            Assert.Equal("定时停止", AutomationProperties.GetName(button));
            Assert.Same(view.FindResource("App.Button.ToolbarValue"), button.Style?.BasedOn);
            Assert.Contains(button.Style!.Triggers, trigger =>
                trigger is DataTrigger dataTrigger &&
                dataTrigger.Binding is Binding binding &&
                binding.Path?.Path == "HasActiveStopTimer");
            Assert.Same(view.FindResource("App.Button.ToolbarValue"), speedButton.Style);
            Assert.Equal(new CornerRadius(12), button.CornerRadius);
            Assert.Equal(new CornerRadius(12), speedButton.CornerRadius);
            Assert.Null(view.FindName("StopTimerPillBorder"));
            Assert.Null(view.FindName("SpeedMenuPillBorder"));
            Assert.Equal(
                "StopTimerRemainingText",
                button.GetBindingExpression(ContentControl.ContentProperty)?.ParentBinding.Path.Path);
            Assert.Equal(
                "SpeakSpeed",
                speedButton.GetBindingExpression(ContentControl.ContentProperty)?.ParentBinding.Path.Path);
            Assert.Same(button, WpfUiFlyoutPlacement.GetPlacementTarget(flyout));
            Assert.Same(view.FindResource("App.Feedback.FlyoutHost"), flyout.Style);
            Assert.Equal("自定义定时停止分钟数", AutomationProperties.GetName(customMinutes));
            Assert.Equal("取消", cancelButton.Content);
            Assert.Equal("应用", applyButton.Content);
            Assert.Equal(1, Grid.GetColumn(cancelButton));
            Assert.Equal(2, Grid.GetColumn(applyButton));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(flyout, button =>
                button.Content is string content &&
                content.Contains("当前段", StringComparison.Ordinal)));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(flyout, button =>
                button.Content is string content &&
                content.Contains("当前章节", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void Player_stop_timer_uses_lightweight_preset_choices()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "Playback",
            "Components",
            "PlayerView.xaml");
        var document = XDocument.Load(path);
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var choices = document.Descendants()
            .Where(element => element.Name == presentation + "Button")
            .Where(element => (string?)element.Attribute("Content") is not null)
            .Where(element => (string?)element.Attribute("Content") is "15 分钟" or "30 分钟" or "45 分钟" or "60 分钟" or "90 分钟")
            .ToArray();

        Assert.Equal(
            ["15 分钟", "30 分钟", "45 分钟", "60 分钟", "90 分钟"],
            choices.Select(choice => (string)choice.Attribute("Content")!).ToArray());
        Assert.All(choices, choice =>
        {
            Assert.Equal("{StaticResource Player.StopTimerChoice}", (string?)choice.Attribute("Style"));
            Assert.Equal(
                ((string)choice.Attribute("Content")!)[..2],
                (string?)choice.Attribute("Tag"));
            Assert.Equal("72", (string?)choice.Attribute("MinWidth"));
            Assert.Equal("36", (string?)choice.Attribute("Height"));
        });
    }

    [Fact]
    public void Player_stop_timer_choice_style_marks_only_the_current_preset()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var view = new PlayerView();
            var style = Assert.IsType<Style>(view.FindResource("Player.StopTimerChoice"));
            var active = new Button
            {
                Style = style,
                Tag = "30",
                DataContext = new StopTimerChoiceState("30")
            };
            var inactive = new Button
            {
                Style = style,
                Tag = "15",
                DataContext = new StopTimerChoiceState("30")
            };
            var window = new Window
            {
                Content = new StackPanel
                {
                    Children = { active, inactive }
                },
                Width = 240,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            };
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var accent = Assert.IsType<SolidColorBrush>(
                    view.FindResource("App.Brush.Accent.Subtle")).Color;
                Assert.Equal(accent, Assert.IsType<SolidColorBrush>(active.Background).Color);
                Assert.NotEqual(accent, Assert.IsType<SolidColorBrush>(inactive.Background).Color);
            }
            finally
            {
                GalleryThemeRuntime.Apply(GalleryTheme.Light);
                window.Close();
            }
        });
    }

    private sealed record StopTimerChoiceState(string StopTimerPresetMinutesText);

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NovelSpeaker.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("NovelSpeaker repository root was not found.");
    }
}
