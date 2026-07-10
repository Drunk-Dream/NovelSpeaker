using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Input;

public sealed class KeyboardShortcutCoordinator : IKeyboardShortcutCoordinator
{
    private readonly IGuardedNavigationService _navigation;
    private readonly ITextFilePicker _textFilePicker;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly PlayerViewModel _playerViewModel;

    public KeyboardShortcutCoordinator(
        IGuardedNavigationService navigation,
        ITextFilePicker textFilePicker,
        LibraryViewModel libraryViewModel,
        PlayerViewModel playerViewModel)
    {
        _navigation = navigation;
        _textFilePicker = textFilePicker;
        _libraryViewModel = libraryViewModel;
        _playerViewModel = playerViewModel;
    }

    public async Task<bool> TryHandleAsync(
        Key key,
        ModifierKeys modifiers,
        KeyboardShortcutContext context,
        CancellationToken cancellationToken)
    {
        var action = KeyboardShortcutPolicy.Resolve(key, modifiers, context);
        if (action is null)
        {
            return false;
        }

        if (action == KeyboardShortcutAction.ImportTextFile)
        {
            var filePath = await _textFilePicker.PickSingleTextFileAsync(cancellationToken).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return true;
            }

            if (await _navigation.NavigateWithHierarchyAsync(typeof(LibraryPage), null, cancellationToken).ConfigureAwait(true))
            {
                await _libraryViewModel.ImportFilesAsync([filePath], cancellationToken).ConfigureAwait(true);
            }

            return true;
        }

        if (action == KeyboardShortcutAction.OpenSettings)
        {
            await _navigation.NavigateWithHierarchyAsync(typeof(SettingsPage), null, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.NavigateBack)
        {
            if (!await _navigation.GoBackAsync(cancellationToken).ConfigureAwait(true))
            {
                await _navigation.NavigateWithHierarchyAsync(typeof(LibraryPage), null, cancellationToken).ConfigureAwait(true);
            }

            return true;
        }

        if (action == KeyboardShortcutAction.TogglePlayback)
        {
            await ExecuteAsync(_playerViewModel.TogglePlayPauseCommand, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.PreviousSegment)
        {
            await ExecuteAsync(_playerViewModel.PreviousSegmentCommand, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.NextSegment)
        {
            await ExecuteAsync(_playerViewModel.NextSegmentCommand, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.PreviousChapter)
        {
            await ExecuteAsync(_playerViewModel.PreviousChapterCommand, cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (action == KeyboardShortcutAction.NextChapter)
        {
            await ExecuteAsync(_playerViewModel.NextChapterCommand, cancellationToken).ConfigureAwait(true);
            return true;
        }

        return false;
    }

    private static async Task ExecuteAsync(IAsyncRelayCommand command, CancellationToken cancellationToken)
    {
        if (command.CanExecute(null))
        {
            await command.ExecuteAsync(null).WaitAsync(cancellationToken).ConfigureAwait(true);
        }
    }
}
