using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class WpfFailureDiagnosticsTests
{
    [Fact]
    public async Task Failure_diagnostics_capture_png_visual_tree_and_window_state_in_a_temp_directory()
    {
        await WpfTestHost.RunInStaAsync(() =>
        {
            var outputDirectory = Path.Combine(
                Path.GetTempPath(),
                "NovelSpeakerWpfDiagnostics",
                Guid.NewGuid().ToString("N"));
            var text = new TextBlock
            {
                Text = "diagnostic fixture"
            };
            AutomationProperties.SetName(text, "Diagnostic fixture");
            var root = new Border
            {
                Width = 160,
                Height = 80,
                Background = System.Windows.Media.Brushes.White,
                Child = text
            };

            try
            {
                using var host = new WpfControlHost(root);
                host.MeasureArrange(new Size(160, 80));
                WpfFailureDiagnostics.TryWriteToDirectory(
                    outputDirectory,
                    new InvalidOperationException("fixture failure"),
                    [root]);

                Assert.True(File.Exists(Path.Combine(outputDirectory, "failure.png")));
                Assert.True(File.Exists(Path.Combine(outputDirectory, "visual-tree.txt")));
                Assert.True(File.Exists(Path.Combine(outputDirectory, "window-state.json")));
                Assert.Contains("fixture failure", File.ReadAllText(Path.Combine(outputDirectory, "visual-tree.txt")));
                Assert.Contains("Diagnostic fixture", File.ReadAllText(Path.Combine(outputDirectory, "visual-tree.txt")));
            }
            finally
            {
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, recursive: true);
                }
            }

            return Task.CompletedTask;
        });
    }
}
