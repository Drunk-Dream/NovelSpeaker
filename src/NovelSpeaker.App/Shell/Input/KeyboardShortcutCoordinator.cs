using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Shell.Input;

public sealed class KeyboardShortcutCoordinator : IKeyboardShortcutCoordinator
{
    private readonly IAppNavigator _navigation;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly PlayerViewModel _playerViewModel;

    public KeyboardShortcutCoordinator(
        IAppNavigator navigation,
        IPresentationFileDialogService fileDialogs,
        LibraryViewModel libraryViewModel,
        PlayerViewModel playerViewModel)
    {
        _navigation = navigation;
        _fileDialogs = fileDialogs;
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
            var filePath = await _fileDialogs.PickOpenFileAsync(
                new PresentationFileDialogOptions("Text files (*.txt)|*.txt|All files (*.*)|*.*"),
                cancellationToken).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return true;
            }

            if (await _navigation.NavigateAsync(AppRoutes.Library, cancellationToken).ConfigureAwait(true))
            {
                await _libraryViewModel.ImportFilesAsync([filePath], cancellationToken).ConfigureAwait(true);
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
            if (!await _navigation.NavigateBackAsync(cancellationToken).ConfigureAwait(true))
            {
                await _navigation.NavigateAsync(AppRoutes.Library, cancellationToken).ConfigureAwait(true);
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
