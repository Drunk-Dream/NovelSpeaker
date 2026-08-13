using System.Collections.Specialized;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
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
    [Fact]
    public async Task OpenMiniPlayerCommand_uses_required_desktop_launcher()
    {
        var launcher = new FakeMiniPlayerLauncher();
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(PlaybackSnapshot.Idle),
            new FakeBookPlaybackContentService(null, null),
            miniPlayerLauncher: launcher);

        await viewModel.OpenMiniPlayerCommand.ExecuteAsync(null);

        Assert.Equal(1, launcher.OpenCount);
    }

    [Fact]
    public async Task SelectRuleCommand_changes_rule_without_losing_context()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
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
            ruleService: new FakeTtsRuleQueries(
                [
                    new TtsRuleSummary(1, "默认规则", true, true, null),
                    new TtsRuleSummary(2, "备用规则", true, false, null),
                    new TtsRuleSummary(3, "已禁用规则", false, false, null)
                ]));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.DoesNotContain(viewModel.Rules, rule => rule.Id == 3);
        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[1]);

        Assert.Equal(2, coordinator.LastChangedRuleId);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
    }

    [Fact]
    public async Task SelectRuleCommand_ignores_current_rule()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
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
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[0]);

        Assert.Null(coordinator.LastChangedRuleId);
    }

    [Fact]
    public async Task ApplySpeakSpeedCommand_changes_speed_with_current_context()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
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
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.ToggleSpeedMenuCommand.Execute(null);
        viewModel.SpeedEditorText = "18";
        await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

        Assert.Equal(18, coordinator.LastChangedSpeakSpeed);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
        Assert.Equal(18, settingsService.Settings.DefaultSpeakSpeed);
    }

    [Fact]
    public async Task ApplySpeakSpeedCommand_updates_global_speed_when_session_already_uses_the_value()
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
    public async Task ApplySpeakSpeedCommand_enforces_domain_boundaries_and_projects_invalid_input()
    {
        var coordinator = new FakePlaybackCoordinator(PlaybackSnapshot.Idle);
        var settingsService = new FakeAppSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(null, null),
            settingsService: settingsService);

        await viewModel.LoadAsync(CancellationToken.None);

        foreach (var (input, expected) in new[] { ("1", 1), ("20", 20) })
        {
            viewModel.SpeedEditorText = input;
            await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);
            Assert.Equal(expected, viewModel.SpeakSpeed);
            Assert.Equal(expected, settingsService.Settings.DefaultSpeakSpeed);
        }

        foreach (var input in new[] { "0", "21", "invalid" })
        {
            var previousSpeed = viewModel.SpeakSpeed;
            var previousSetting = settingsService.Settings.DefaultSpeakSpeed;
            var previousChange = coordinator.LastChangedSpeakSpeed;
            viewModel.SpeedEditorText = input;

            await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

            Assert.Contains("1 到 20", viewModel.SpeedEditorErrorText, StringComparison.Ordinal);
            Assert.Equal(previousSpeed, viewModel.SpeakSpeed);
            Assert.Equal(previousSetting, settingsService.Settings.DefaultSpeakSpeed);
            Assert.Equal(previousChange, coordinator.LastChangedSpeakSpeed);
        }
    }

    [Fact]
    public async Task CommitSpeakSpeedAsync_does_not_write_or_overwrite_state_after_activation_is_cancelled()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
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

    [Fact]
    public async Task IncreaseAndDecreaseSpeakSpeedCommands_debounce_and_apply_only_the_latest_speed()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
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
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
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

    [Fact]
    public async Task HandleNavigationAsync_same_book_open_paused_request_keeps_real_time_session()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
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
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(0, coordinator.OpenPausedCallCount);
        Assert.Equal(PlaybackState.Playing, viewModel.CurrentPlaybackState);
    }

    [Fact]
    public async Task HandleNavigationAsync_restores_paused_session_when_rule_becomes_available_again()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Stopped,
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
            "当前没有可用的 TTS 规则，请先前往规则页选择或导入规则。",
            false,
            false,
            "作者甲",
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            ruleService: new FakeTtsRuleQueries(
                [new TtsRuleSummary(1, "默认规则", true, true, null)]));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.Equal(1, coordinator.OpenPausedCallCount);
        Assert.True(viewModel.HasAvailableRule);
        Assert.False(viewModel.ShowNoRuleState);
        Assert.True(viewModel.ShowPlaybackControls);
        Assert.Equal(PlaybackState.Paused, viewModel.CurrentPlaybackState);
    }

}
