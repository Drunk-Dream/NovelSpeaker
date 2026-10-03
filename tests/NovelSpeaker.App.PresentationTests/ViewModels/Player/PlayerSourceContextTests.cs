using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Features.Playback.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.TestKit.Navigation;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels.Player;

public sealed class PlayerSourceContextTests
{
    [Fact]
    public async Task Source_invalidation_rejects_first_catalog_load_that_completes_after_idle()
    {
        var source = new DeferredContent();
        var controller = new PlayerContentController(new PlaybackBackedBookDetailsQuery(source), source, new Scheduler());
        var loading = controller.EnsureContentLoadedAsync(PlaybackSnapshot.Idle with
        {
            BookId = "book",
            ChapterIndex = 0,
            SourceContext = source.Book.SourceContext
        }, CancellationToken.None);
        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await controller.EnsureContentLoadedAsync(PlaybackSnapshot.Idle, CancellationToken.None);
        }
        finally { source.Result.TrySetResult(source.Book); }
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(controller.LoadedBook);
        Assert.Empty(controller.Chapters);
        Assert.Empty(controller.Segments);
        Assert.Null(controller.CurrentChapterItem);
    }

    [Fact]
    public async Task Reopening_same_book_after_catalog_replacement_discards_old_segments_and_selection()
    {
        var content = new Content();
        var controller = new PlayerContentController(new PlaybackBackedBookDetailsQuery(content), content, new Scheduler());
        var snapshot = PlaybackSnapshot.Idle with { BookId = "book", ChapterIndex = 0, SourceContext = content.Book.SourceContext };
        await controller.EnsureContentLoadedAsync(snapshot, CancellationToken.None);
        Assert.Equal("旧正文", Assert.Single(controller.Segments).Text);
        controller.ApplyChapterSelection(0, true);
        content.Book = Content.Create("new", "新章", "新正文");
        await controller.EnsureContentLoadedAsync(snapshot with { SourceContext = content.Book.SourceContext }, CancellationToken.None);
        Assert.Equal("新章", Assert.Single(controller.Chapters).Title);
        Assert.False(controller.Chapters[0].IsSelected);
        Assert.Equal("新正文", Assert.Single(controller.Segments).Text);
        await controller.EnsureContentLoadedAsync(PlaybackSnapshot.Idle, CancellationToken.None);
        Assert.Empty(controller.Chapters);
        Assert.Empty(controller.Segments);
        Assert.Null(controller.CurrentChapterItem);
    }

    private sealed class Content : IBookPlaybackContentService
    {
        public PlaybackBookContent Book { get; set; } = Create("old", "旧章", "旧正文");
        public static PlaybackBookContent Create(string version, string title, string text) =>
            new("book", "书", [PlaybackChapterContent.FromLoaded(0, title, [new SpeechSegment(0, 0, text.Length, text, text)], version)],
                SourceContext: new("local:book", version));
        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult<PlaybackBookContent?>(Book);
        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackChapterContent?>(Book.Chapters[0]);
    }

    private sealed class Scheduler : IUiScheduler
    {
        public bool CheckAccess() => true;
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) { action(); return Task.CompletedTask; }
        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class DeferredContent : IBookPlaybackContentService
    {
        public PlaybackBookContent Book { get; } = Content.Create("old", "旧章", "旧正文");
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PlaybackBookContent?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return Result.Task.WaitAsync(cancellationToken);
        }
        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackChapterContent?>(Book.Chapters[0]);
    }
}

public sealed partial class PlayerViewModelTests
{
    [Fact]
    public async Task Source_invalidation_snapshot_clears_visible_content_and_management_selection()
    {
        var chapter = PlaybackChapterContent.FromLoaded(0, "旧章", [new SpeechSegment(0, 0, 3, "旧正文", "旧正文")], "old");
        var context = new ActiveSourceContext("local:book-1", "old");
        var playback = new FakePlaybackCoordinator(PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Paused,
            BookId = "book-1",
            ChapterIndex = 0,
            SegmentCount = 1,
            SourceContext = context
        });
        var viewModel = CreateViewModel(playback, new FakeBookPlaybackContentService(
            new PlaybackBookContent("book-1", "书", [chapter], SourceContext: context), chapter));
        try
        {
            await viewModel.LoadAsync(CancellationToken.None);
            await viewModel.HandleNavigationAsync(
                new PlayerNavigationRequest("book-1", AppRoutes.Library, PlayerNavigationMode.ReturnToCurrentSession), CancellationToken.None);
            Assert.Single(viewModel.Segments);
            await viewModel.HandleChapterClickAsync(viewModel.Chapters[0], DesktopSelectionModifiers.Control, CancellationToken.None);
            Assert.Equal(1, viewModel.SelectedChapterCount);
            var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.PropertyChanged += (_, _) =>
            {
                if (viewModel.Chapters.Count == 0 && viewModel.Segments.Count == 0 && viewModel.SelectedChapterCount == 0)
                    cleared.TrySetResult();
            };
            playback.Publish(PlaybackSnapshot.Idle);
            await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(viewModel.Chapters);
            Assert.Empty(viewModel.Segments);
            Assert.Equal(0, viewModel.SelectedChapterCount);
            Assert.Null(viewModel.CurrentChapterItem);
            Assert.Null(viewModel.CurrentSegmentItem);
        }
        finally { viewModel.OnPageNavigatedFrom(); }
    }
}
