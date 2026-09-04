using System.Windows.Input;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Shell.Input;

public sealed class KeyboardShortcutCoordinator : IKeyboardShortcutCoordinator
{
    private readonly IAppNavigator _navigation;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly IKeyboardShortcutTargetRegistry _targets;

    public KeyboardShortcutCoordinator(
        IAppNavigator navigation,
        IPresentationFileDialogService fileDialogs,
        IKeyboardShortcutTargetRegistry targets)
    {
        _navigation = navigation;
        _fileDialogs = fileDialogs;
        _targets = targets;
    }

    public async Task<bool> TryHandleAsync(
        Key key,
        ModifierKeys modifiers,
        KeyboardShortcutContext context,
        CancellationToken cancellationToken)
    {
        if (key == Key.Escape &&
            modifiers == ModifierKeys.None &&
            !context.IsTextEditing &&
            !context.IsTransientUiOpen &&
            context.TransientEscapeHandler?.TryHandleEscape() == true)
        {
            return true;
        }

        var action = KeyboardShortcutPolicy.Resolve(key, modifiers, context);
        if (action is null)
        {
            return false;
        }

        if (action == KeyboardShortcutAction.ImportTextFile)
        {
            var filePath = await _fileDialogs.PickOpenFileAsync(
                new PresentationFileDialogOptions("Text files (*.txt)|*.txt|All files (*.*)|*.*"),
                cancellationToken).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return true;
            }

            if (await _navigation.NavigateAsync(AppRoutes.Library, cancellationToken).ConfigureAwait(true))
            {
                await HandleCurrentTargetAsync(
                    KeyboardShortcutAction.ImportTextFile,
                    filePath,
                    cancellationToken).ConfigureAwait(true);
            }

            return true;
        }

        if (action == KeyboardShortcutAction.OpenSettings)
        {
            await _navigation.NavigateAsync(AppRoutes.Settings, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.NavigateBack)
        {
            await _navigation.NavigateBackAsync(cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.TogglePlayback)
        {
            await HandleCurrentTargetAsync(action.Value, null, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.PreviousSegment)
        {
            await HandleCurrentTargetAsync(action.Value, null, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.NextSegment)
        {
            await HandleCurrentTargetAsync(action.Value, null, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.PreviousChapter)
        {
            await HandleCurrentTargetAsync(action.Value, null, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.NextChapter)
        {
            await HandleCurrentTargetAsync(action.Value, null, cancellationToken).ConfigureAwait(true);
            return true;
        }

        return false;
    }

    private async Task HandleCurrentTargetAsync(
        KeyboardShortcutAction action,
        string? argument,
        CancellationToken cancellationToken)
    {
        var target = _targets.Current;
        if (target is not null)
        {
            await target.HandleKeyboardShortcutAsync(action, argument, cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(true);
        }
    }
}
