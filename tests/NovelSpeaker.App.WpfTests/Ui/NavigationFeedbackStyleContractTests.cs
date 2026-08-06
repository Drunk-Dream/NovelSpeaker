using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using NovelSpeaker.App.Shared.Theming.Components;
using NovelSpeaker.StyleGallery;
using Xunit;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class NavigationFeedbackStyleContractTests
{
    [Fact]
    public void Feedback_resources_are_explicit_named_styles_in_the_control_theme_dictionary()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "ControlThemes",
            "NavigationFeedbackStyles.xaml");
        var document = XDocument.Load(path);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = document.Root?.Elements().ToArray() ?? [];
        var keys = resources
            .Select(resource => resource.Attribute(xaml + "Key")?.Value ?? string.Empty)
            .ToArray();

        Assert.Equal(
            [
                "App.Feedback.ProgressBar",
                "App.Feedback.SurfaceBase",
                "App.Feedback.FlyoutSurface",
                "App.Feedback.DialogShell",
                "App.Feedback.SnackbarContent",
                "App.Feedback.StatusBase",
                "App.Feedback.Loading",
                "App.Feedback.Error",
                "App.Feedback.NoResult"
            ],
            keys);
        Assert.All(resources, resource => Assert.Equal("Style", resource.Name.LocalName));
        Assert.DoesNotContain(resources, resource => resource.Attribute(xaml + "Key") is null);
        Assert.DoesNotContain(
            resources.SelectMany(resource => resource.Descendants()),
            element => element.Name.LocalName == "Style" && element.Attribute(xaml + "Key") is null);
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Navigation_feedback_gallery_keeps_progress_and_slider_type_boundaries(
        GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("navigation-feedback");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            Assert.Empty(FindDescendants<Menu>(scene));

            var progress = FindDescendants<ProgressBar>(scene).Single(control =>
                AutomationProperties.GetAutomationId(control) == "feedback-progress");
            var slider = FindDescendants<Slider>(scene).Single(control =>
                AutomationProperties.GetAutomationId(control) == "feedback-slider");
            Assert.NotSame(progress.Style, slider.Style);
            Assert.Equal(typeof(ProgressBar), progress.Style?.TargetType);
            Assert.Equal(typeof(Slider), slider.Style?.TargetType);
            Assert.True(progress.ActualHeight >= progress.MinHeight);
            Assert.True(slider.ActualHeight > 0);
        });
    }

    [Fact]
    public void Dialog_shell_owns_default_cancel_and_escape_dismissal_semantics()
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(GalleryTheme.Light);
            var scene = GallerySceneRegistry.Build("navigation-feedback");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var dialog = FindDescendants<DialogShell>(scene).Single();
            var buttons = FindDescendants<WpfButton>(dialog);
            var confirm = buttons.Single(button =>
                AutomationProperties.GetAutomationId(button) == "dialog-confirm");
            var cancel = buttons.Single(button =>
                AutomationProperties.GetAutomationId(button) == "dialog-cancel");
            Assert.True(confirm.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.False(dialog.Focusable);
            Assert.False(dialog.IsTabStop);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(dialog));
            var confirmed = false;
            dialog.Confirmed += (_, _) => confirmed = true;
            confirm.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
            cancel.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
            Assert.True(dialog.IsConfirmed);
            Assert.False(dialog.IsCancelled);
            Assert.True(dialog.IsDismissed);
            Assert.True(confirmed);

            var escapeDialog = new DialogShell();
            using var escapeHost = WpfWindowHost.Show(new Window
            {
                Content = escapeDialog,
                Width = 500,
                Height = 260,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            escapeHost.Window.UpdateLayout();
            var cancelled = false;
            escapeDialog.Cancelled += (_, _) => cancelled = true;

            var dismissed = false;
            escapeDialog.Dismissed += (_, _) => dismissed = true;
            var keyArgs = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(escapeDialog)!,
                0,
                Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Source = escapeDialog
            };
            escapeDialog.RaiseEvent(keyArgs);

            Assert.True(keyArgs.Handled);
            Assert.True(escapeDialog.IsDismissed);
            Assert.True(escapeDialog.IsCancelled);
            Assert.True(dismissed);
            Assert.True(cancelled);
        });
    }

    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Feedback_surfaces_and_request_states_have_named_templates_and_accessible_content(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("navigation-feedback");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var surfaces = FindDescendants<FeedbackSurfaceBase>(scene);
            Assert.Equal(6, surfaces.Count);
            Assert.All(surfaces, surface =>
            {
                Assert.NotNull(surface.Style);
                Assert.NotNull(surface.Template);
                Assert.True(surface.ActualWidth > 0);
                Assert.True(surface.ActualHeight > 0);
            });
            Assert.All(
                surfaces.Where(surface => surface is not DialogShell),
                surface =>
                {
                    Assert.False(surface.Focusable);
                    Assert.False(surface.IsTabStop);
                });
            Assert.NotNull(FindDescendants<FlyoutSurface>(scene).Single().Effect);
            Assert.NotNull(FindDescendants<DialogShell>(scene).Single().Effect);
            Assert.Same(
                global::System.Windows.Application.Current!.FindResource("App.Elevation.Medium"),
                FindDescendants<FlyoutSurface>(scene).Single().Effect);
            Assert.Same(
                global::System.Windows.Application.Current!.FindResource("App.Elevation.High"),
                FindDescendants<DialogShell>(scene).Single().Effect);
            Assert.All(
                FindDescendants<FeedbackStatusBase>(scene),
                state => Assert.NotEmpty(FindDescendants<WpfTextBlock>(state)));

            var snackbarClose = FindDescendants<WpfButton>(scene).Single(button =>
                AutomationProperties.GetAutomationId(button) == "snackbar-close");
            Assert.Same(
                global::System.Windows.Application.Current!.FindResource("Provider.Button"),
                snackbarClose.Style?.BasedOn);
        });
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
