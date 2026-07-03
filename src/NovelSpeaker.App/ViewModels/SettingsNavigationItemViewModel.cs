using System.Windows.Input;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace NovelSpeaker.App.ViewModels;

public sealed record SettingsNavigationItemViewModel(
    string Title,
    SymbolRegular IconSymbol,
    ICommand Command);
