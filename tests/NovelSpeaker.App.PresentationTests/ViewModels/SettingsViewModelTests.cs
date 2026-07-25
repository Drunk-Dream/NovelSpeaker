using NovelSpeaker.App.Features.Settings;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

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
                Assert.Equal([SettingsNavigationIcon.Playback, SettingsNavigationIcon.TtsRules], group.Items.Select(item => item.Icon));
            },
            group =>
            {
                Assert.Equal("文本处理", group.Title);
                Assert.Equal(["导入与文本", "章节规则"], group.Items.Select(item => item.Title));
                Assert.Equal([SettingsNavigationIcon.ImportText, SettingsNavigationIcon.ChapterRules], group.Items.Select(item => item.Icon));
            },
            group =>
            {
                Assert.Equal("应用", group.Title);
                Assert.Equal(["缓存与数据", "外观", "诊断与关于"], group.Items.Select(item => item.Title));
                Assert.Equal(
                    [SettingsNavigationIcon.CacheAndData, SettingsNavigationIcon.Appearance, SettingsNavigationIcon.Diagnostics],
                    group.Items.Select(item => item.Icon));
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

    [Fact]
    public void OpenCacheAndDataCommand_navigates_to_cache_and_data_page()
    {
        var navigationService = new FakeNavigationService();
        var viewModel = new SettingsViewModel(navigationService);

        viewModel.OpenCacheAndDataCommand.Execute(null);

        Assert.Equal(typeof(CacheAndDataPage), navigationService.LastNavigationPageType);
        Assert.True(navigationService.LastUsedHierarchyNavigation);
    }

    private sealed class FakeNavigationService : ITestNavigationService
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
