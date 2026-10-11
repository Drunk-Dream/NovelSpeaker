using NovelSpeaker.TestKit.Speech;
using NovelSpeaker.Domain.Speech.Providers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed partial class PlayerViewTests
{
    private sealed class FakePlaybackCoordinator : IPlaybackSession
    {
        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                ChapterTitle = chapterIndex == 0 ? "第一章" : "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }

        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                SegmentIndex = segmentIndex
            });
            return Task.CompletedTask;
        }

        public Task NextSegmentAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                SegmentIndex = CurrentSnapshot.SegmentIndex + 1,
                SegmentCount = Math.Max(CurrentSnapshot.SegmentCount, CurrentSnapshot.SegmentIndex + 2)
            });
            return Task.CompletedTask;
        }

        public Task PreviousSegmentAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                SegmentIndex = Math.Max(CurrentSnapshot.SegmentIndex - 1, 0)
            });
            return Task.CompletedTask;
        }

        public Task NextChapterAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = CurrentSnapshot.ChapterIndex + 1,
                ChapterTitle = "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }

        public Task PreviousChapterAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = Math.Max(CurrentSnapshot.ChapterIndex - 1, 0),
                ChapterTitle = "第一章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeProviderAsync(ProviderId providerId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetVolume(double volume) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent? _book;
        private readonly PlaybackChapterContent? _chapter;

        public FakeBookPlaybackContentService(PlaybackBookContent? book, PlaybackChapterContent? chapter)
        {
            _book = book;
            _chapter = chapter;
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            return Task.FromResult(_chapter);
        }
    }

    private sealed class FakeProviders : TestCurrentSpeechProvider
    {
        private readonly IReadOnlyList<SpeechProviderInstance> _providers;

        public FakeProviders(IReadOnlyList<SpeechProviderInstance> rules)
        {
            _providers = rules;
        }

        public override Task<IReadOnlyList<SpeechProviderInstance>> GetAvailableAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SpeechProviderInstance>>(_providers.Where(provider => ProviderConfigurationValidator.Validate(provider).IsValid).ToArray());
    }

    private sealed class FakeAppSettingsStore : IAppSettingsService
    {
        public FakeAppSettingsStore(AppSettings settings)
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
        public ProjectedUiError Project(Exception exception)
        {
            return new ProjectedUiError(exception.Message, UiMessageSeverity.Error, false);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowInformation(string title, string message)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
        }
    }

    private sealed class FakeNavigationService : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(false);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);
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

}
