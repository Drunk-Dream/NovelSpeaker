using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovelSpeaker.TestKit.Wpf;

internal static class WpfFailureDiagnostics
{
    public static void TryWrite(
        string testName,
        Exception exception,
        IReadOnlyList<DependencyObject> roots)
    {
        TryWriteToDirectory(
            Path.Combine(LocateRepositoryRoot(), "TestResults", "wpf-diagnostics", Sanitize(testName)),
            exception,
            roots);
    }

    internal static void TryWriteToDirectory(
        string outputDirectory,
        Exception exception,
        IReadOnlyList<DependencyObject> roots)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);

            var windows = global::System.Windows.Application.Current?.Windows
                .Cast<Window>()
                .ToArray() ?? [];
            try
            {
                var windowStates = windows.Select(CreateWindowState).ToArray();
                File.WriteAllText(
                    Path.Combine(outputDirectory, "window-state.json"),
                    JsonSerializer.Serialize(
                        new { exception = new { type = exception.GetType().FullName, exception.Message }, windows = windowStates },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                File.WriteAllText(Path.Combine(outputDirectory, "window-state.json"), "{\"diagnosticsError\":\"window-state\"}");
            }

            try
            {
                var visualTree = new StringBuilder();
                visualTree.AppendLine($"Exception: {exception.GetType().FullName}: {exception.Message}");
                visualTree.AppendLine(exception.StackTrace);
                foreach (var root in roots.Concat(windows.OfType<DependencyObject>()).Distinct())
                {
                    try
                    {
                        AppendVisualTree(visualTree, root, 0);
                    }
                    catch (Exception treeException)
                    {
                        visualTree.AppendLine($"Visual tree unavailable: {treeException.GetType().FullName}: {treeException.Message}");
                    }
                }

                File.WriteAllText(Path.Combine(outputDirectory, "visual-tree.txt"), visualTree.ToString());
            }
            catch
            {
                File.WriteAllText(
                    Path.Combine(outputDirectory, "visual-tree.txt"),
                    $"Exception: {exception.GetType().FullName}: {exception.Message}");
            }

            try
            {
                var renderRoot = roots
                    .OfType<FrameworkElement>()
                    .FirstOrDefault(root => root.ActualWidth > 0 || root.DesiredSize.Width > 0)
                    ?? windows.FirstOrDefault(window => window.IsVisible);
                if (renderRoot is null)
                {
                    return;
                }

                var width = renderRoot.ActualWidth > 0 ? renderRoot.ActualWidth : renderRoot.DesiredSize.Width;
                var height = renderRoot.ActualHeight > 0 ? renderRoot.ActualHeight : renderRoot.DesiredSize.Height;
                if (width <= 0 || height <= 0)
                {
                    return;
                }

                var bitmap = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Ceiling(width)),
                    Math.Max(1, (int)Math.Ceiling(height)),
                    96,
                    96,
                    PixelFormats.Pbgra32);
                bitmap.Render(renderRoot);
                using var stream = File.Create(Path.Combine(outputDirectory, "failure.png"));
                new PngBitmapEncoder { Frames = { BitmapFrame.Create(bitmap) } }.Save(stream);
            }
            catch
            {
                // Keep the textual diagnostics even if a WPF visual cannot be rendered.
            }
        }
        catch
        {
            // Diagnostics must never replace the original assertion or exception.
        }
    }

    private static WindowDiagnosticState CreateWindowState(Window window) => new(
        window,
        window.GetType().FullName,
        window.Left,
        window.Top,
        window.ActualWidth,
        window.ActualHeight,
        window.WindowState.ToString(),
        window.Visibility.ToString(),
        window.IsVisible,
        PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M11 ?? 1);

    private sealed record WindowDiagnosticState(
        [property: JsonIgnore] Window Window,
        string? Type,
        double Left,
        double Top,
        double Width,
        double Height,
        string State,
        string Visibility,
        bool IsVisible,
        double Dpi)
    {
    }

    private static void AppendVisualTree(StringBuilder builder, DependencyObject node, int depth)
    {
        var indent = new string(' ', depth * 2);
        var element = node as FrameworkElement;
        builder.Append(indent).Append(node.GetType().FullName);
        if (element is not null)
        {
            builder.Append($" name={element.Name} id={AutomationProperties.GetAutomationId(element)} automationName={AutomationProperties.GetName(element)}")
                .Append($" size={element.ActualWidth:0.##}x{element.ActualHeight:0.##} visibility={element.Visibility} enabled={element.IsEnabled} focused={element.IsKeyboardFocused}");
        }

        builder.AppendLine();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            AppendVisualTree(builder, VisualTreeHelper.GetChild(node, index), depth + 1);
        }
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NovelSpeaker.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}
