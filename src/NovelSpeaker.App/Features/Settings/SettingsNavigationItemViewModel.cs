using System.Windows.Input;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace NovelSpeaker.App.Features.Settings;

public sealed record SettingsNavigationItemViewModel(
    string Title,
    SymbolRegular IconSymbol,
    ICommand Command);
