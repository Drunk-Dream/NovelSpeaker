using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.StyleGallery;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class RulesSharedGalleryTests
{
    [Theory]
    [InlineData(GalleryTheme.Light)]
    [InlineData(GalleryTheme.Dark)]
    public void Scene_uses_formal_rule_items_for_all_required_fixture_states(GalleryTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            GalleryThemeRuntime.EnsureProviderResources();
            GalleryThemeRuntime.Apply(theme);
            var scene = GallerySceneRegistry.Build("rules-shared");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = scene,
                Width = GalleryRenderSettings.WindowWidth,
                Height = GalleryRenderSettings.WindowHeight,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var rules = VisualTreeTestHelper.FindDescendants<RuleListItemView>(scene)
                .ToDictionary(AutomationProperties.GetAutomationId, StringComparer.Ordinal);
            Assert.Equal(9, rules.Count);
            Assert.Equal(
                ["Chapter", "Regex", "TTS"],
                rules.Values.Select(AutomationProperties.GetHelpText).Distinct().Order(StringComparer.Ordinal));
            Assert.False(rules["rules-shared-tts-default"].IsSortable);
            Assert.False(rules["rules-shared-tts-disabled"].IsRuleEnabled);
            Assert.True(rules["rules-shared-tts-selected"].IsSelected);
            Assert.True(rules["rules-shared-chapter-sortable"].IsSortable);
            Assert.False(rules["rules-shared-chapter-sortable"].CanMoveUp);
            Assert.True(rules["rules-shared-chapter-dragging"].IsDragging);
            Assert.Equal(
                RuleDropPlacement.Before,
                rules["rules-shared-regex-insert-before"].DropPlacement);
            Assert.Equal(
                RuleDropPlacement.After,
                rules["rules-shared-regex-insert-after"].DropPlacement);

            var contextRule = rules["rules-shared-regex-context-menu"];
            var menu = Assert.IsType<ContextMenu>(contextRule.ContextMenu);
            menu.PlacementTarget = contextRule;
            menu.IsOpen = true;
            menu.UpdateLayout();
            Assert.Equal(
                ["导出到文件", "复制到剪切板", "上移", "下移", "删除"],
                menu.Items.OfType<MenuItem>()
                    .Where(item => item.Visibility == Visibility.Visible)
                    .Select(item => item.Header));
            menu.IsOpen = false;

            var focusRule = rules["rules-shared-tts-focus"];
            Assert.True(focusRule.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
            Assert.True(focusRule.IsKeyboardFocusWithin);
            Assert.NotNull(GallerySceneRenderer.Render(
                scene,
                GallerySceneRegistry.All.Single(definition => definition.Name == "rules-shared")));
        });
    }

    [Fact]
    public async Task Screenshot_generator_writes_rules_shared_family_manifest()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var output = new TemporaryRulesOutputDirectory();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var options = GalleryCommandLineOptions.Parse(
            [
                "--screenshot",
                "--theme",
                "all",
                "--scene",
                "rules-shared",
                "--output",
                output.Path
            ]);
            var window = new GalleryWindow();
            try
            {
                WpfWindowHost.Show(window);
                var manifest = await new GalleryScreenshotGenerator()
                    .GenerateAsync(window, options, cancellation.Token);

                Assert.Equal("rules-shared", manifest.ArtifactId);
                Assert.Equal(2, manifest.Scenes.Count);
                Assert.Equal(["Dark", "Light"], manifest.Scenes.Select(item => item.Theme).Order());
                Assert.All(manifest.Scenes, entry =>
                {
                    Assert.Equal("rules-shared", entry.Scene);
                    Assert.True(File.Exists(Path.Combine(output.Path, entry.Png)));
                    Assert.Equal(64, entry.Sha256.Length);
                });
                var json = await File.ReadAllTextAsync(output.ManifestPath, cancellation.Token);
                using var document = JsonDocument.Parse(json);
                Assert.Equal("rules-shared", document.RootElement
                    .GetProperty("artifactId")
                    .GetString());
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    private sealed class TemporaryRulesOutputDirectory : IDisposable
    {
        public TemporaryRulesOutputDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "NovelSpeakerRulesGalleryTests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public string ManifestPath => System.IO.Path.Combine(Path, "manifest.json");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
