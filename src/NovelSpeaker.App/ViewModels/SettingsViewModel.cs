using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Pages;
using Wpf.Ui;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    public SettingsViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
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
    private void OpenPlaybackSettings()
    {
        _navigationService.NavigateWithHierarchy(typeof(PlaybackSettingsPage));
    }

    [RelayCommand]
    private void OpenTtsRules()
    {
        _navigationService.NavigateWithHierarchy(typeof(TtsRulesPage));
    }

    [RelayCommand]
    private void OpenImportTextSettings()
    {
        _navigationService.NavigateWithHierarchy(typeof(ImportTextSettingsPage));
    }

    [RelayCommand]
    private void OpenChapterRules()
    {
        _navigationService.NavigateWithHierarchy(typeof(ChapterRulesPage));
    }

    [RelayCommand]
    private void OpenCacheAndData()
    {
        _navigationService.NavigateWithHierarchy(typeof(CacheAndDataPage));
    }

    [RelayCommand]
    private void OpenAppearanceSettings()
    {
        _navigationService.NavigateWithHierarchy(typeof(AppearanceSettingsPage));
    }

    [RelayCommand]
    private void OpenDiagnosticsAbout()
    {
        _navigationService.NavigateWithHierarchy(typeof(DiagnosticsAboutPage));
    }
}
