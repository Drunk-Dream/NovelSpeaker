using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shell.Navigation;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace NovelSpeaker.App.Features.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppNavigator _navigator;

    public SettingsViewModel(IAppNavigator navigator)
    {
        _navigator = navigator;
        Groups =
        [
            new SettingsNavigationGroupViewModel(
                "常用",
                [
                    new SettingsNavigationItemViewModel("播放设置", SymbolRegular.PlayCircle24, OpenPlaybackSettingsCommand),
                    new SettingsNavigationItemViewModel("TTS 规则", SymbolRegular.Speaker124, OpenTtsRulesCommand)
                ]),
            new SettingsNavigationGroupViewModel(
                "文本处理",
                [
                    new SettingsNavigationItemViewModel("导入与文本", SymbolRegular.DocumentText24, OpenImportTextSettingsCommand),
                    new SettingsNavigationItemViewModel("章节规则", SymbolRegular.TextBulletListSquare24, OpenChapterRulesCommand)
                ]),
            new SettingsNavigationGroupViewModel(
                "应用",
                [
                    new SettingsNavigationItemViewModel("缓存与数据", SymbolRegular.Database24, OpenCacheAndDataCommand),
                    new SettingsNavigationItemViewModel("外观", SymbolRegular.DarkTheme24, OpenAppearanceSettingsCommand),
                    new SettingsNavigationItemViewModel("诊断与关于", SymbolRegular.Info24, OpenDiagnosticsAboutCommand)
                ])
        ];
    }

    public IReadOnlyList<SettingsNavigationGroupViewModel> Groups { get; }

    [RelayCommand]
    private Task OpenPlaybackSettingsAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.PlaybackSettings, cancellationToken);
    }

    [RelayCommand]
    private Task OpenTtsRulesAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.TtsRules, cancellationToken);
    }

    [RelayCommand]
    private Task OpenImportTextSettingsAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.ImportTextSettings, cancellationToken);
    }

    [RelayCommand]
    private Task OpenChapterRulesAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.ChapterRules, cancellationToken);
    }

    [RelayCommand]
    private Task OpenCacheAndDataAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.CacheAndData, cancellationToken);
    }

    [RelayCommand]
    private Task OpenAppearanceSettingsAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.AppearanceSettings, cancellationToken);
    }

    [RelayCommand]
    private Task OpenDiagnosticsAboutAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.DiagnosticsAbout, cancellationToken);
    }
}
