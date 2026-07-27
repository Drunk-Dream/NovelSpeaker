using Microsoft.Extensions.DependencyInjection;
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
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Navigation;

[Collection("WpfDispatcher")]
public sealed class NavigationPageLifecycleTests
{
    [Fact]
    public void BookDetailsPage_captures_strongly_typed_navigation_request()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<BookDetailsPage>();
                page.DataContext = new BookDetailsNavigationRequest("book-42");

                page.OnNavigatedToAsync().GetAwaiter().GetResult();

                Assert.Equal("book-42", page.LastRequest?.BookId);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void PlayerPage_captures_strongly_typed_navigation_request()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = new PlayerViewModel(
                new FakePlaybackCoordinator(new PlaybackSnapshot(
                    PlaybackState.Paused,
                    "book-7",
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
                    false)),
                new NovelSpeaker.App.WpfTests.FakeActiveCacheCoordinator(),
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-7", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                    PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsService(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new FakePlayerAutoScrollCoordinator());
            var page = new PlayerPage(viewModel);
            page.DataContext = new PlayerNavigationRequest("book-7", PlayerNavigationMode.ReturnToCurrentSession);

            page.OnNavigatedToAsync().GetAwaiter().GetResult();

            Assert.Equal("book-7", page.LastRequest?.BookId);
            Assert.Equal(PlayerNavigationMode.ReturnToCurrentSession, page.LastRequest?.Mode);
        });
    }

    [Fact]
    public void Leaving_player_page_does_not_stop_the_playback_session()
    {
        WpfTestHost.RunInSta(() =>
        {
            var playback = new FakePlaybackCoordinator(new PlaybackSnapshot(
                PlaybackState.Playing,
                "book-7",
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
            var viewModel = new PlayerViewModel(
                playback,
                new NovelSpeaker.App.WpfTests.FakeActiveCacheCoordinator(),
                new FakeBookPlaybackContentService(
                    new PlaybackBookContent("book-7", "示例小说", [PlaybackChapterContent.FromLoaded(0, "第一章", [])], "作者甲"),
                    PlaybackChapterContent.FromLoaded(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
                new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
                new FakeAppSettingsService(AppSettings.Default),
                new FakeAppFeedbackService(),
                new FakeNavigationService(),
                new FakePlayerAutoScrollCoordinator());
            var page = new PlayerPage(viewModel)
            {
                DataContext = new PlayerNavigationRequest("book-7", PlayerNavigationMode.ReturnToCurrentSession)
            };

            page.OnNavigatedToAsync().GetAwaiter().GetResult();
            page.OnNavigatedFromAsync().GetAwaiter().GetResult();

            Assert.Equal(0, playback.StopCallCount);
            Assert.Equal(PlaybackState.Playing, playback.CurrentSnapshot.State);
        });
    }

    private sealed class FakePlaybackCoordinator : IPlaybackSession
    {
        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public int StopCallCount { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCallCount++;
            return Task.CompletedTask;
        }
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
        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent _book;
        private readonly PlaybackChapterContent _chapter;

        public FakeBookPlaybackContentService(PlaybackBookContent book, PlaybackChapterContent chapter)
        {
            _book = book;
            _chapter = chapter;
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackBookContent?>(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackChapterContent?>(_chapter);
        }
    }

    private sealed class FakePlayerAutoScrollCoordinator : IPlayerAutoScrollCoordinator
    {
        public PlayerAutoScrollState State => PlayerAutoScrollState.AutoCentering;

        public bool ShouldAutoCenter => true;

        public bool ShowReturnToCurrentSegment => false;

        public int PendingRestoreVersion => 0;

        public event EventHandler? StateChanged
        {
            add { }
            remove { }
        }

        public void NotifyUserScrollInput()
        {
        }

        public void NotifyPassiveScrollChange()
        {
        }

        public void BeginScrollbarDrag()
        {
        }

        public void EndScrollbarDrag()
        {
        }

        public void BeginProgrammaticScroll()
        {
        }

        public void EndProgrammaticScroll()
        {
        }

        public void ResumeAutoCenter()
        {
        }

        public void ResetForPageLeave()
        {
        }
    }

    private sealed class FakeTtsRuleQueries : ITtsRuleQueries
    {
        private readonly IReadOnlyList<TtsRuleSummary> _rules;

        public FakeTtsRuleQueries(IReadOnlyList<TtsRuleSummary> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);
        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }
        public AppSettings Current => Settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            Settings = (Settings with
            {
                DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? Settings.DefaultSpeakSpeed
            }).Normalize();
            return Task.FromResult(Settings);
        }
    }

    private sealed class FakeAppFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);
        public void ShowProjectedNotification(string title, ProjectedUiError projected) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken) => Task.FromResult(AppConfirmationDecision.Cancel);
    }

    private sealed class FakeNavigationService : ITestNavigationService
    {
        public INavigationView GetNavigationControl() => throw new NotSupportedException();
        public bool GoBack() => false;
        public bool Navigate(Type pageType) => true;
        public bool Navigate(Type pageType, object? dataContext) => true;
        public bool Navigate(string pageIdOrTargetTag) => true;
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;
        public bool NavigateWithHierarchy(Type pageType) => true;
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;
        public void SetNavigationControl(INavigationView navigation) { }
    }
}
