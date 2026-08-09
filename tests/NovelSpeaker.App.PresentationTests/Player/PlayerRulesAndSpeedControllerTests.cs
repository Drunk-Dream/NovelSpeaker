using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Player;

public sealed class PlayerRulesAndSpeedControllerTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("20", 20)]
    public void TryParseSpeakSpeed_accepts_domain_boundaries(string input, int expected)
    {
        using var controller = CreateController();

        var valid = controller.TryParseSpeakSpeed(input, out var speed, out var error);

        Assert.True(valid);
        Assert.Equal(expected, speed);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("invalid")]
    public void TryParseSpeakSpeed_rejects_values_outside_domain_contract(string input)
    {
        using var controller = CreateController();

        var valid = controller.TryParseSpeakSpeed(input, out _, out var error);

        Assert.False(valid);
        Assert.Contains("1 到 20", error);
    }

    [Fact]
    public async Task LoadRulesAsync_projects_only_enabled_rules()
    {
        using var controller = CreateController(new StubRuleQueries
        {
            Rules =
            [
                new TtsRuleSummary(1, "启用规则", true, true, null),
                new TtsRuleSummary(2, "禁用规则", false, false, null)
            ]
        });

        var rules = await controller.LoadRulesAsync(CancellationToken.None);

        Assert.Collection(rules, rule => Assert.Equal(1, rule.Id));
    }

    private static PlayerRulesAndSpeedController CreateController(StubRuleQueries? queries = null)
    {
        return new PlayerRulesAndSpeedController(
            new StubPlaybackSession(),
            queries ?? new StubRuleQueries(),
            new StubSettingsService(),
            new StubFeedbackService(),
            TimeProvider.System);
    }

    private sealed class StubPlaybackSession : IPlaybackSession
    {
        public PlaybackSnapshot CurrentSnapshot => PlaybackSnapshot.Idle;

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetVolume(double volume) { }
        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubRuleQueries : ITtsRuleQueries
    {
        public IReadOnlyList<TtsRuleSummary> Rules { get; init; } = [];

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Rules);
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubSettingsService : IAppSettingsService
    {
        public AppSettings Current => AppSettings.Default;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppSettings.Default);
        }
    }

    private sealed class StubFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception)
        {
            return new ProjectedUiError(exception.Message, UiMessageSeverity.Error, false);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
        }
    }
}
