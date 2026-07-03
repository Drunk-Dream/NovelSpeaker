using NovelSpeaker.App.Pages;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void Groups_expose_expected_titles_and_order()
    {
        var viewModel = new SettingsViewModel(new FakeNavigationService());

        Assert.Collection(
            viewModel.Groups,
            group =>
            {
                Assert.Equal("常用", group.Title);
                Assert.Equal(["播放设置", "TTS 规则"], group.Items.Select(item => item.Title));
                Assert.Equal([SymbolRegular.PlayCircle24, SymbolRegular.Speaker124], group.Items.Select(item => item.IconSymbol));
            },
            group =>
            {
                Assert.Equal("文本处理", group.Title);
                Assert.Equal(["导入与文本", "章节规则"], group.Items.Select(item => item.Title));
                Assert.Equal([SymbolRegular.DocumentText24, SymbolRegular.TextBulletListSquare24], group.Items.Select(item => item.IconSymbol));
            },
            group =>
            {
                Assert.Equal("应用", group.Title);
                Assert.Equal(["缓存与数据", "外观", "诊断与关于"], group.Items.Select(item => item.Title));
                Assert.Equal([SymbolRegular.Database24, SymbolRegular.DarkTheme24, SymbolRegular.Info24], group.Items.Select(item => item.IconSymbol));
            });
    }

    [Fact]
    public void OpenPlaybackSettingsCommand_navigates_to_playback_settings_page()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = new SettingsViewModel(navigationService);

        viewModel.OpenPlaybackSettingsCommand.Execute(null);

        Assert.Equal(typeof(PlaybackSettingsPage), navigationService.LastNavigationPageType);
        Assert.True(navigationService.LastUsedHierarchyNavigation);
    }

    [Fact]
    public void OpenDiagnosticsAboutCommand_navigates_to_diagnostics_page()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = new SettingsViewModel(navigationService);

        viewModel.OpenDiagnosticsAboutCommand.Execute(null);

        Assert.Equal(typeof(DiagnosticsAboutPage), navigationService.LastNavigationPageType);
        Assert.True(navigationService.LastUsedHierarchyNavigation);
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public Type? LastNavigationPageType { get; private set; }

        public bool LastUsedHierarchyNavigation { get; private set; }

        public INavigationView? NavigationControl { get; private set; }

        public INavigationView GetNavigationControl()
        {
            return NavigationControl!;
        }

        public bool GoBack()
        {
            return false;
        }

        public bool Navigate(Type pageType)
        {
            LastNavigationPageType = pageType;
            LastUsedHierarchyNavigation = false;
            return true;
        }

        public bool Navigate(Type pageType, object? dataContext)
        {
            LastNavigationPageType = pageType;
            LastUsedHierarchyNavigation = false;
            return true;
        }

        public bool Navigate(string pageIdOrTargetTag)
        {
            return true;
        }

        public bool Navigate(string pageIdOrTargetTag, object? dataContext)
        {
            return true;
        }

        public bool NavigateWithHierarchy(Type pageType)
        {
            LastNavigationPageType = pageType;
            LastUsedHierarchyNavigation = true;
            return true;
        }

        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            LastNavigationPageType = pageType;
            LastUsedHierarchyNavigation = true;
            return true;
        }

        public void SetNavigationControl(INavigationView navigation)
        {
            NavigationControl = navigation;
        }
    }
}
