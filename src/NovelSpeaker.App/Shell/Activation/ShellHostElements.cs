using System.Windows;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shell.Activation;

public sealed record ShellHostElements(
    Window Window,
    NavigationView NavigationView,
    NavigationViewItem LibraryItem,
    NavigationViewItem SettingsItem,
    NavigationViewItem PlaybackItem,
    ContentDialogHost ContentDialogHost,
    SnackbarPresenter SnackbarPresenter);
