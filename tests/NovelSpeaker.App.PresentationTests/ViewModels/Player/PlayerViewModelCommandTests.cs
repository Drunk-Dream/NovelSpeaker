using NovelSpeaker.TestKit.Speech;
using NovelSpeaker.Domain.Speech.Providers;
using System.Collections.Specialized;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Common;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels.Player;

public sealed partial class PlayerViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Paused_session_without_current_provider_requires_selection_unless_audio_is_loaded(bool audioLoaded, bool hasOtherProvider)
    {
        var snapshot = PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Paused,
            BookId = "book",
            ProviderId = TestSpeechProviders.Id(1),
            HasAvailableProvider = true,
            HasLoadedAudio = audioLoaded
        };
        var model = CreateViewModel(new FakePlaybackCoordinator(snapshot),
            new FakeBookPlaybackContentService(null, null),
            ruleService: new FakeProviders(hasOtherProvider ? [TestSpeechProviders.Item(2, "Other")] : []),
            settingsService: new FakeAppSettingsService(AppSettings.Default));
        await model.LoadAsync(CancellationToken.None);
        Assert.Equal(!audioLoaded, model.ShowNoProviderState);
        Assert.Equal(audioLoaded, model.CanTogglePlayPause);
        Assert.All(model.Providers, provider => Assert.False(provider.IsCurrent));
        if (!audioLoaded)
            Assert.Equal(hasOtherProvider ? "尚未选择语音服务" : "尚无可用的语音服务", model.ProviderUnavailableTitle);
        model.OnPageNavigatedFrom();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task No_current_provider_preserves_navigation_and_exposes_available_choice_state(bool hasProvider)
    {
        var navigation = new FakeNavigationService();
        var model = CreateViewModel(new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            new FakeBookPlaybackContentService(null, null),
            ruleService: new FakeProviders(hasProvider ? [TestSpeechProviders.Item(1, "Service")] : []),
            settingsService: new FakeAppSettingsService(AppSettings.Default), navigationService: navigation);
        await model.LoadAsync(CancellationToken.None);
        Assert.True(model.ShowNoProviderState);
        Assert.False(model.CanTogglePlayPause);
        Assert.Equal(hasProvider, model.HasProviders);
        Assert.All(model.Providers, item => Assert.False(item.IsCurrent));
        Assert.Equal(hasProvider ? "尚未选择语音服务" : "尚无可用的语音服务", model.ProviderUnavailableTitle);
        await model.OpenSpeechServicesCommand.ExecuteAsync(null);
        Assert.Equal(AppRoutes.SpeechServices, navigation.LastNavigationRoute);
        model.OnPageNavigatedFrom();
    }

    private async Task OpenMiniPlayerCommand_uses_required_desktop_launcher()
    {
        var launcher = new FakeMiniPlayerLauncher();
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            new FakeBookPlaybackContentService(null, null),
            miniPlayerLauncher: launcher);

        await viewModel.OpenMiniPlayerCommand.ExecuteAsync(null);

        Assert.Equal(1, launcher.OpenCount);
    }

    private async Task SelectProviderCommand_changes_rule_without_losing_context()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            TestSpeechProviders.Id(1),
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            ruleService: new FakeProviders(
                [
                    TestSpeechProviders.Item(1, "默认规则", true),
                    TestSpeechProviders.Item(2, "备用规则", true),
                    TestSpeechProviders.Item(3, "未配置服务", false)
                ]));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.DoesNotContain(viewModel.Providers, rule => rule.Id == TestSpeechProviders.Id(3));
        await viewModel.SelectProviderCommand.ExecuteAsync(viewModel.Providers[1]);

        Assert.Equal(TestSpeechProviders.Id(2), coordinator.LastChangedProviderId);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
    }

    private async Task SelectProviderCommand_ignores_current_rule()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            TestSpeechProviders.Id(1),
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await viewModel.SelectProviderCommand.ExecuteAsync(viewModel.Providers[0]);

        Assert.Null(coordinator.LastChangedProviderId);
    }

    private async Task ApplySpeakSpeedCommand_changes_speed_with_current_context()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            TestSpeechProviders.Id(1),
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false));
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            settingsService: settingsService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.ToggleSpeedMenuCommand.Execute(null);
        viewModel.SpeedEditorText = "18";
        await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

        Assert.Equal(18, coordinator.LastChangedSpeakSpeed);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
        Assert.Equal(18, settingsService.Settings.DefaultSpeakSpeed);
    }

    private async Task ApplySpeakSpeedCommand_updates_global_speed_when_session_already_uses_the_value()
    {
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Paused,
            BookId = "book-1",
            SpeakSpeed = 10
        });
        var settingsService = new FakeAppSettingsService(
            AppSettings.Default with { DefaultSpeakSpeed = 12 });
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(null, null),
            settingsService: settingsService);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.ToggleSpeedMenuCommand.Execute(null);
        viewModel.SpeedEditorText = "10";

        await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

        Assert.Equal(10, settingsService.Settings.DefaultSpeakSpeed);
        Assert.Null(coordinator.LastChangedSpeakSpeed);
    }

    [Fact]
    public async Task Active_snapshot_zero_speed_is_displayed_and_preserved_when_opening()
    {
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle with
        {
            BookId = "book-1",
            SpeakSpeed = 0,
            State = PlaybackState.Paused
        });
        var viewModel = CreateViewModel(coordinator, new FakeBookPlaybackContentService(null, null));
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.Equal(0, viewModel.SpeakSpeed);
        Assert.Equal("0", viewModel.SpeedEditorText);
    }

    private async Task ApplySpeakSpeedCommand_enforces_domain_boundaries_and_projects_invalid_input()
    {
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle);
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(null, null),
            settingsService: settingsService);

        await viewModel.LoadAsync(CancellationToken.None);

        foreach (var (input, expected) in new[] { ("0", 0), ("50", 50), ("100", 100) })
        {
            viewModel.SpeedEditorText = input;
            await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);
            Assert.Equal(expected, viewModel.SpeakSpeed);
            Assert.Equal(expected, settingsService.Settings.DefaultSpeakSpeed);
        }

        foreach (var input in new[] { "-1", "101", "invalid" })
        {
            var previousSpeed = viewModel.SpeakSpeed;
            var previousSetting = settingsService.Settings.DefaultSpeakSpeed;
            var previousChange = coordinator.LastChangedSpeakSpeed;
            viewModel.SpeedEditorText = input;

            await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

            Assert.Contains("0 到 100", viewModel.SpeedEditorErrorText, StringComparison.Ordinal);
            Assert.Equal(previousSpeed, viewModel.SpeakSpeed);
            Assert.Equal(previousSetting, settingsService.Settings.DefaultSpeakSpeed);
            Assert.Equal(previousChange, coordinator.LastChangedSpeakSpeed);
        }
    }

    private async Task CommitSpeakSpeedAsync_does_not_write_or_overwrite_state_after_activation_is_cancelled()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            TestSpeechProviders.Id(1),
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false));
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(null, null),
            settingsService: settingsService);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SpeedEditorText = "18";
        using var activationCancellation = new CancellationTokenSource();
        activationCancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => viewModel.CommitSpeakSpeedAsync(activationCancellation.Token));

        Assert.False(settingsService.UpdateStarted.Task.IsCompleted);
        Assert.Null(coordinator.LastChangedSpeakSpeed);
        Assert.Equal(10, viewModel.SpeakSpeed);
        Assert.Equal("18", viewModel.SpeedEditorText);
    }

    private async Task IncreaseAndDecreaseSpeakSpeedCommands_debounce_and_apply_only_the_latest_speed()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            TestSpeechProviders.Id(1),
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false));
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            settingsService: settingsService,
            timeProvider: timeProvider);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.IncreaseSpeakSpeedCommand.Execute(null);
        viewModel.IncreaseSpeakSpeedCommand.Execute(null);
        viewModel.DecreaseSpeakSpeedCommand.Execute(null);

        Assert.Null(coordinator.LastChangedSpeakSpeed);
        Assert.Equal(11, viewModel.SpeakSpeed);
        Assert.Equal("11", viewModel.SpeedEditorText);

        timeProvider.Advance(TimeSpan.FromMilliseconds(499));
        await Task.Yield();
        Assert.Null(coordinator.LastChangedSpeakSpeed);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await coordinator.WaitForSpeedChangeAsync();

        Assert.Equal(11, coordinator.LastChangedSpeakSpeed);
        Assert.Equal(11, settingsService.Settings.DefaultSpeakSpeed);
    }

    private async Task HandleNavigationAsync_same_book_open_paused_request_keeps_real_time_session()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(PlaybackState.Playing,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            TestSpeechProviders.Id(1),
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(0, coordinator.OpenPausedCallCount);
        Assert.Equal(PlaybackState.Playing, viewModel.CurrentPlaybackState);
    }

    private async Task HandleNavigationAsync_restores_paused_session_when_rule_becomes_available_again()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(PlaybackState.Stopped,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            null,
            null,
            10,
            0,
            0,
            "当前没有可用的 语音服务，请先前往语音服务管理完成配置。",
            false,
            false,
            "作者甲",
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            ruleService: new FakeProviders(
                [TestSpeechProviders.Item(1, "默认规则", true)]));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.Equal(1, coordinator.OpenPausedCallCount);
        Assert.True(viewModel.HasAvailableProvider);
        Assert.False(viewModel.ShowNoProviderState);
        Assert.True(viewModel.ShowPlaybackControls);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
    }

    [Fact]
    public async Task Player_command_rule_and_desktop_contracts_cover_context_actions()
    {
        await OpenMiniPlayerCommand_uses_required_desktop_launcher();
        await SelectProviderCommand_changes_rule_without_losing_context();
        await SelectProviderCommand_ignores_current_rule();
    }

    [Fact]
    public async Task Player_command_speed_contracts_cover_normalization_cancellation_and_debounce()
    {
        await ApplySpeakSpeedCommand_changes_speed_with_current_context();
        await ApplySpeakSpeedCommand_updates_global_speed_when_session_already_uses_the_value();
        await ApplySpeakSpeedCommand_enforces_domain_boundaries_and_projects_invalid_input();
        await CommitSpeakSpeedAsync_does_not_write_or_overwrite_state_after_activation_is_cancelled();
        await IncreaseAndDecreaseSpeakSpeedCommands_debounce_and_apply_only_the_latest_speed();
    }

    [Fact]
    public async Task Player_command_navigation_contracts_preserve_session_and_restore_paused_playback()
    {
        await HandleNavigationAsync_same_book_open_paused_request_keeps_real_time_session();
        await HandleNavigationAsync_restores_paused_session_when_rule_becomes_available_again();
    }

}
