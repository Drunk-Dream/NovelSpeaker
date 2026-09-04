using System.Windows.Input;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Shell.Navigation;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Input;

public sealed class KeyboardShortcutCoordinatorTests
{
    [Fact]
    public async Task Playback_shortcut_is_sent_to_the_current_activation_target()
    {
        var targets = new KeyboardShortcutTargetRegistry();
        var oldTarget = new RecordingShortcutTarget();
        var currentTarget = new RecordingShortcutTarget();
        using var oldRegistration = targets.Register(oldTarget);
        using var currentRegistration = targets.Register(currentTarget);
        var coordinator = new KeyboardShortcutCoordinator(
            new FakeNavigationService(),
            new FakeFileDialogService(),
            targets);

        var handled = await coordinator.TryHandleAsync(
            Key.Right,
            ModifierKeys.Control,
            new KeyboardShortcutContext(true, false, false),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Empty(oldTarget.Actions);
        Assert.Equal([KeyboardShortcutAction.NextSegment], currentTarget.Actions);
    }

    [Fact]
    public async Task Import_shortcut_uses_the_target_activated_by_navigation()
    {
        var targets = new KeyboardShortcutTargetRegistry();
        var target = new RecordingShortcutTarget();
        IDisposable? registration = null;
        var navigation = new FakeNavigationService
        {
            OnNavigate = route =>
            {
                Assert.Same(AppRoutes.Library, route);
                registration = targets.Register(target);
                return true;
            }
        };
        var coordinator = new KeyboardShortcutCoordinator(
            navigation,
            new FakeFileDialogService { SelectedPath = "D:\\Books\\demo.txt" },
            targets);

        try
        {
            var handled = await coordinator.TryHandleAsync(
                Key.O,
                ModifierKeys.Control,
                new KeyboardShortcutContext(false, false, false),
                CancellationToken.None);

            Assert.True(handled);
            Assert.Equal(
                [(KeyboardShortcutAction.ImportTextFile, "D:\\Books\\demo.txt")],
                target.Arguments);
        }
        finally
        {
            registration?.Dispose();
        }
    }

    private sealed class RecordingShortcutTarget : IKeyboardShortcutTarget
    {
        public List<KeyboardShortcutAction> Actions { get; } = [];

        public List<(KeyboardShortcutAction Action, string? Argument)> Arguments { get; } = [];

        public Task<bool> HandleKeyboardShortcutAsync(
            KeyboardShortcutAction action,
            string? argument,
            CancellationToken cancellationToken)
        {
            Actions.Add(action);
            Arguments.Add((action, argument));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeNavigationService : IAppNavigator
    {
        public Func<AppRoute, bool>? OnNavigate { get; init; }

        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(false);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(OnNavigate?.Invoke(route) ?? false);
    }

    private sealed class FakeFileDialogService : IPresentationFileDialogService
    {
        public string? SelectedPath { get; init; }

        public Task<string?> PickOpenFileAsync(
            PresentationFileDialogOptions options,
            CancellationToken cancellationToken) =>
            Task.FromResult(SelectedPath);

        public Task<string?> PickSaveFileAsync(
            PresentationFileDialogOptions options,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(
            PresentationFolderDialogOptions options,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
