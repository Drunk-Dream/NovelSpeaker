using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovelSpeaker.StyleGallery;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using WpfUiButton = Wpf.Ui.Controls.Button;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed partial class BookDetailsPageTests
{
    [Fact]
    public async Task Book_details_floating_icon_renders_real_light_and_dark_interaction_states()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var originalTheme = FloatingIconVisualAssertions.CaptureTheme();
            try
            {
                GalleryThemeRuntime.EnsureProviderResources();
                foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
                {
                    GalleryThemeRuntime.Apply(theme);
                    var viewModel = CreateViewModel();
                    PopulateLayoutState(viewModel, chapterCount: 24);
                    var page = new BookDetailsPage(viewModel, new FakeNavigationGuardService());
                    var button = Assert.IsType<WpfUiButton>(page.FindName("LocateCurrentChapterButton"));

                    Assert.Equal(Visibility.Collapsed, button.Visibility);
                    using var host = FloatingIconVisualHost.Show(page, new Size(1280, 760));
                    button.Visibility = Visibility.Visible;
                    host.MeasureArrange();
                    await WpfTestHost.DrainDispatcherAsync();

                    Assert.Equal("定位到当前章节", button.ToolTip);
                    Assert.Equal("定位到当前章节", AutomationProperties.GetName(button));
                    Assert.Equal(SymbolRegular.TargetArrow24, Assert.IsType<SymbolIcon>(button.Icon).Symbol);
                    Assert.True(
                        button.ActualWidth > 0,
                        $"Book Details button did not enter the visual tree: visibility={button.Visibility}, " +
                        $"isVisible={button.IsVisible}, page={page.ActualWidth}x{page.ActualHeight}, " +
                        $"root={host.Root.ActualWidth}x{host.Root.ActualHeight}, " +
                        $"parent={VisualTreeHelper.GetParent(button)?.GetType().Name ?? "<none>"}.");
                    FloatingIconVisualAssertions.AssertAllStates(host, button, "Book Details 定位按钮");
                }
            }
            finally
            {
                GalleryThemeRuntime.Apply(originalTheme);
            }
        });
    }
}

public sealed partial class PlayerViewTests
{
    [Fact]
    public async Task Player_floating_icons_render_real_light_and_dark_interaction_states()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var originalTheme = FloatingIconVisualAssertions.CaptureTheme();
            try
            {
                GalleryThemeRuntime.EnsureProviderResources();
                foreach (var theme in new[] { GalleryTheme.Light, GalleryTheme.Dark })
                {
                    GalleryThemeRuntime.Apply(theme);
                    var view = new PlayerView
                    {
                        DataContext = new PlayerViewLayoutTestContext(
                            new System.Collections.ObjectModel.ObservableCollection<PlayerChapterItemViewModel>
                            {
                                new(0, "第一章")
                            },
                            new System.Collections.ObjectModel.ObservableCollection<PlayerSegmentItemViewModel>
                            {
                                new(0, 0, "用于 FloatingIcon 视觉回归的脱敏正文。")
                                {
                                    IsCurrent = true,
                                    VisualOpacity = 1d
                                }
                            },
                            showReturnToCurrentSegment: true)
                    };
                    var locateButton = Assert.IsType<WpfUiButton>(view.FindName("LocateCurrentChapterButton"));
                    var returnButton = Assert.IsType<WpfUiButton>(view.FindName("ReturnToCurrentSegmentButton"));

                    Assert.Equal(Visibility.Collapsed, locateButton.Visibility);
                    locateButton.Visibility = Visibility.Visible;
                    Assert.Equal("定位到当前章节", locateButton.ToolTip);
                    Assert.Equal("返回当前段落", returnButton.ToolTip);
                    Assert.Equal("定位到当前章节", AutomationProperties.GetName(locateButton));
                    Assert.Equal("返回当前段落", AutomationProperties.GetName(returnButton));
                    Assert.Equal(SymbolRegular.TargetArrow24, Assert.IsType<SymbolIcon>(locateButton.Icon).Symbol);
                    Assert.Equal(SymbolRegular.TargetArrow24, Assert.IsType<SymbolIcon>(returnButton.Icon).Symbol);

                    using var host = FloatingIconVisualHost.Show(view, new Size(1280, 760));
                    await WpfTestHost.DrainDispatcherAsync();
                    Assert.Equal(Visibility.Visible, returnButton.Visibility);
                    FloatingIconVisualAssertions.AssertAllStates(host, locateButton, "Player 定位按钮");
                    await WpfTestHost.DrainDispatcherAsync();
                    FloatingIconVisualAssertions.AssertAllStates(host, returnButton, "Player 返回按钮");
                    await WpfTestHost.DrainDispatcherAsync();
                }
            }
            finally
            {
                GalleryThemeRuntime.Apply(originalTheme);
            }
        });
    }
}

internal sealed class FloatingIconVisualHost : IDisposable
{
    private readonly WpfWindowHost _windowHost;

    private FloatingIconVisualHost(WpfWindowHost windowHost, FrameworkElement root, Visual renderRoot, Size size)
    {
        _windowHost = windowHost;
        Root = root;
        RenderRoot = renderRoot;
        Size = size;
        Root.Measure(size);
        Root.Arrange(new Rect(new Point(), size));
        Root.UpdateLayout();
    }

    internal FrameworkElement Root { get; }

    private Visual RenderRoot { get; }

    private Size Size { get; }

    internal void MeasureArrange()
    {
        Root.Measure(Size);
        Root.Arrange(new Rect(new Point(), Size));
        Root.UpdateLayout();
    }

    internal static FloatingIconVisualHost Show(FrameworkElement content, Size size)
    {
        FrameworkElement root;
        if (content is Page)
        {
            root = content;
        }
        else
        {
            root = new AdornerDecorator
            {
                Width = size.Width,
                Height = size.Height,
                Child = content
            };
        }
        var window = new Window
        {
            Content = root,
            Width = size.Width,
            Height = size.Height,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow
        };
        var windowHost = WpfWindowHost.Show(window);
        window.UpdateLayout();

        return new FloatingIconVisualHost(
            windowHost,
            root,
            content is Page ? window : root,
            size);
    }

    internal BitmapSource Render()
    {
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(Size.Width)),
            Math.Max(1, (int)Math.Round(Size.Height)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(RenderRoot);
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose() => _windowHost.Dispose();
}

internal static class FloatingIconVisualAssertions
{
    private static readonly DependencyPropertyKey IsMouseOverKey =
        (DependencyPropertyKey)(typeof(UIElement)
            .GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null)
         ?? throw new InvalidOperationException("WPF IsMouseOver property key was not found."));

    private static readonly DependencyPropertyKey IsPressedKey =
        (DependencyPropertyKey)(typeof(ButtonBase)
            .GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null)
         ?? throw new InvalidOperationException("WPF IsPressed property key was not found."));

    internal static void AssertAllStates(
        FloatingIconVisualHost host,
        WpfUiButton button,
        string scene)
    {
        var root = host.Root;
        SetReadOnlyValue(button, IsMouseOverKey, false);
        SetReadOnlyValue(button, IsPressedKey, false);
        Keyboard.ClearFocus();
        Assert.False(button.IsKeyboardFocused, scene + " Rest 前不应拥有键盘焦点");
        root.UpdateLayout();
        var bounds = GetBounds(button, root);
        var icon = Assert.IsType<SymbolIcon>(button.Icon);
        var iconBounds = GetBounds(icon, root);
        Assert.Equal(44, bounds.Width, 3);
        Assert.Equal(44, bounds.Height, 3);
        AssertCentered(bounds, iconBounds, scene + " Rest");
        var rest = host.Render();
        AssertHitArea(button, scene + " Rest");
        var restBounds = bounds;
        var restIconBounds = iconBounds;
        var restEffect = button.Effect;
        var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
            global::System.Windows.Application.Current);
        var raised = ResourceColor(application, "App.Brush.Surface.Raised");
        var hoverSurface = ResourceColor(application, "App.Brush.Interaction.Surface.Hover");
        var pressedSurface = ResourceColor(application, "App.Brush.Interaction.Surface.Pressed");
        var primary = ResourceColor(application, "App.Brush.Text.Primary");
        var hoverForeground = ResourceColor(application, "App.Brush.Interaction.Foreground.Hover");
        var pressedForeground = ResourceColor(application, "App.Brush.Interaction.Foreground.Pressed");
        var focus = ResourceColor(application, "App.Brush.Focus");
        var surfaceSecondary = ResourceColor(application, "App.Brush.Surface.Secondary");
        var accentSubtle = ResourceColor(application, "App.Brush.Accent.Subtle");
        bool? previousAlwaysShowFocusVisual = null;

        try
        {
            AssertSurface(rest, restBounds, raised, scene + " Rest", surfaceSecondary, accentSubtle);
            AssertIconForeground(rest, root, button, primary, scene + " Rest");
            AssertDoesNotContain(rest, Expand(restBounds, 4), focus, scene + " Rest 不应显示 Focus Ring");

            SetReadOnlyValue(button, IsMouseOverKey, true);
            root.UpdateLayout();
            var hover = host.Render();
            AssertInteractionGeometry(root, button, restBounds, restIconBounds, scene + " Hover");
            Assert.Same(restEffect, button.Effect);
            AssertSurface(hover, restBounds, hoverSurface, scene + " Hover", surfaceSecondary, accentSubtle);
            AssertIconForeground(hover, root, button, hoverForeground, scene + " Hover");
            AssertDoesNotContain(hover, Expand(restBounds, 4), focus, scene + " Hover 不应显示 Focus Ring");

            SetReadOnlyValue(button, IsPressedKey, true);
            root.UpdateLayout();
            var pressed = host.Render();
            AssertInteractionGeometry(root, button, restBounds, restIconBounds, scene + " Pressed");
            Assert.Same(restEffect, button.Effect);
            AssertSurface(pressed, restBounds, pressedSurface, scene + " Pressed", surfaceSecondary, accentSubtle);
            AssertIconForeground(pressed, root, button, pressedForeground, scene + " Pressed");
            AssertDoesNotContain(pressed, Expand(restBounds, 4), focus, scene + " Pressed 不应显示 Focus Ring");

            SetReadOnlyValue(button, IsPressedKey, false);
            SetReadOnlyValue(button, IsMouseOverKey, false);
            Keyboard.ClearFocus();
            previousAlwaysShowFocusVisual = ShowFocusVisualForKeyboardNavigation();
            root.UpdateLayout();
            Assert.True(button.Focus(), scene + " 应可接收键盘焦点");
            Assert.True(button.IsKeyboardFocused, scene + " 应获得键盘焦点");
            root.UpdateLayout();
            var focused = host.Render();
            AssertInteractionGeometry(root, button, restBounds, restIconBounds, scene + " Keyboard Focus");
            Assert.Same(restEffect, button.Effect);
            AssertIconForeground(focused, root, button, primary, scene + " Keyboard Focus");
            AssertContains(focused, Expand(restBounds, 4), focus, scene + " Keyboard Focus 应显示 Focus Ring");
        }
        finally
        {
            Keyboard.ClearFocus();
            SetReadOnlyValue(button, IsMouseOverKey, false);
            SetReadOnlyValue(button, IsPressedKey, false);
            root.UpdateLayout();
            if (previousAlwaysShowFocusVisual is { } previous)
            {
                RestoreFocusVisualMode(previous);
            }
        }
    }

    private static void AssertSurface(
        BitmapSource bitmap,
        Rect bounds,
        Color expected,
        string scene,
        Color unexpectedHover,
        Color unexpectedPressed)
    {
        var inner = new Rect(bounds.Left + 6, bounds.Top + 6, bounds.Width - 12, bounds.Height - 12);
        Assert.True(
            Count(bitmap, inner, expected) >= 20,
            $"{scene} 未在最终像素中呈现足够的预期 Surface {expected}。");
        Assert.Equal(0, Count(bitmap, inner, unexpectedHover));
        Assert.Equal(0, Count(bitmap, inner, unexpectedPressed));
    }

    private static void AssertIconForeground(
        BitmapSource bitmap,
        FrameworkElement root,
        WpfUiButton button,
        Color expected,
        string scene)
    {
        var icon = Assert.IsType<SymbolIcon>(button.Icon);
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
        Assert.NotEqual(Colors.Black, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
        Assert.True(icon.ActualWidth > 0 && icon.ActualHeight > 0, scene);
        var iconBounds = GetBounds(icon, root);
        Assert.True(
            Count(bitmap, iconBounds, expected) > 0,
            $"{scene} 未在最终像素中呈现预期图标前景 {expected}。");
    }

    private static void AssertInteractionGeometry(
        FrameworkElement root,
        WpfUiButton button,
        Rect expectedButtonBounds,
        Rect expectedIconBounds,
        string scene)
    {
        Assert.Equal(expectedButtonBounds, GetBounds(button, root));
        var actualIconBounds = GetBounds(Assert.IsType<SymbolIcon>(button.Icon), root);
        Assert.Equal(expectedIconBounds, actualIconBounds);
        AssertCentered(expectedButtonBounds, actualIconBounds, scene);
        AssertHitArea(button, scene);
    }

    private static void AssertCentered(Rect buttonBounds, Rect iconBounds, string scene)
    {
        var buttonCenter = new Point(
            buttonBounds.Left + buttonBounds.Width / 2,
            buttonBounds.Top + buttonBounds.Height / 2);
        var iconCenter = new Point(
            iconBounds.Left + iconBounds.Width / 2,
            iconBounds.Top + iconBounds.Height / 2);
        Assert.InRange(Math.Abs(buttonCenter.X - iconCenter.X), 0, 0.5);
        Assert.InRange(Math.Abs(buttonCenter.Y - iconCenter.Y), 0, 0.5);
    }

    private static void AssertHitArea(WpfUiButton button, string scene)
    {
        foreach (var point in new[]
                 {
                     new Point(3, button.ActualHeight / 2),
                     new Point(button.ActualWidth / 2, button.ActualHeight / 2),
                     new Point(button.ActualWidth - 4, button.ActualHeight / 2)
                 })
        {
            var hit = VisualTreeHelper.HitTest(button, point);
            Assert.NotNull(hit);
            Assert.True(
                IsWithin(button, hit!.VisualHit),
                $"{scene} 在相对按钮坐标 {point} 命中到了按钮外部对象。");
        }

        foreach (var point in new[]
                 {
                     new Point(-1, button.ActualHeight / 2),
                     new Point(button.ActualWidth + 1, button.ActualHeight / 2)
                 })
        {
            var hit = VisualTreeHelper.HitTest(button, point);
            Assert.False(
                hit is not null && IsWithin(button, hit.VisualHit),
                $"{scene} 在按钮边界外的相对坐标 {point} 仍命中了 FloatingIcon 按钮。");
        }
    }

    private static bool IsWithin(DependencyObject ancestor, DependencyObject candidate)
    {
        for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static Color ResourceColor(global::System.Windows.Application application, string key) =>
        Assert.IsType<SolidColorBrush>(application.FindResource(key)).Color;

    private static Rect GetBounds(FrameworkElement element, FrameworkElement root) =>
        new(element.TranslatePoint(new Point(), root), element.RenderSize);

    private static Rect Expand(Rect bounds, double amount) =>
        new(bounds.Left - amount, bounds.Top - amount, bounds.Width + amount * 2, bounds.Height + amount * 2);

    private static int Count(BitmapSource bitmap, Rect bounds, Color expected)
    {
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, bitmap.PixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, bitmap.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right) - 1, left, bitmap.PixelWidth - 1);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom) - 1, top, bitmap.PixelHeight - 1);
        var count = 0;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (ReadPixel(bitmap, new Point(x, y)) == expected)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertContains(
        BitmapSource bitmap,
        Rect bounds,
        Color expected,
        string message)
    {
        Assert.True(Count(bitmap, bounds, expected) > 0, message);
    }

    private static void AssertDoesNotContain(
        BitmapSource bitmap,
        Rect bounds,
        Color unexpected,
        string message)
    {
        Assert.Equal(0, Count(bitmap, bounds, unexpected));
    }

    private static Color ReadPixel(BitmapSource bitmap, Point point)
    {
        var x = Math.Clamp((int)Math.Floor(point.X), 0, bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(point.Y), 0, bitmap.PixelHeight - 1);
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    internal static GalleryTheme CaptureTheme()
    {
        var applicationTheme = ApplicationThemeManager.GetAppTheme();
        if (applicationTheme == ApplicationTheme.Dark)
        {
            return GalleryTheme.Dark;
        }

        if (applicationTheme == ApplicationTheme.Light)
        {
            return GalleryTheme.Light;
        }

        var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
            global::System.Windows.Application.Current);
        return application.Resources.MergedDictionaries.Any(dictionary =>
            dictionary.Source?.OriginalString?.EndsWith(
                "Palette.Dark.xaml",
                StringComparison.OrdinalIgnoreCase) == true)
            ? GalleryTheme.Dark
            : GalleryTheme.Light;
    }

    private static void SetReadOnlyValue(
        DependencyObject target,
        DependencyPropertyKey key,
        bool value)
    {
        target.SetValue(key, value);
    }

    private static bool ShowFocusVisualForKeyboardNavigation()
    {
        var staticProperty = typeof(KeyboardNavigation).GetProperty(
            "AlwaysShowFocusVisual",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(staticProperty);
        var previous = Assert.IsType<bool>(staticProperty!.GetValue(null));
        staticProperty.SetValue(null, true);
        return previous;
    }

    private static void RestoreFocusVisualMode(bool value)
    {
        var staticProperty = typeof(KeyboardNavigation).GetProperty(
            "AlwaysShowFocusVisual",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(staticProperty);
        staticProperty!.SetValue(null, value);
    }
}
