using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed partial class PlaybackCoordinatorTests
{
    [Theory]
    [InlineData(RegexReplacementRulesChangeKind.Saved)]
    [InlineData(RegexReplacementRulesChangeKind.EnabledChanged)]
    [InlineData(RegexReplacementRulesChangeKind.Deleted)]
    [InlineData(RegexReplacementRulesChangeKind.Reordered)]
    [InlineData(RegexReplacementRulesChangeKind.Imported)]
    [InlineData(RegexReplacementRulesChangeKind.Restored)]
    public async Task Committed_regex_mutations_reach_playback_without_a_page(RegexReplacementRulesChangeKind kind)
    {
        var rule = SpeechRule("^第一段$", "旧语音");
        var rules = kind == RegexReplacementRulesChangeKind.Imported ? new List<RegexReplacementRule>() : [rule];
        if (kind == RegexReplacementRulesChangeKind.EnabledChanged) rules[0] = rule with { IsEnabled = false, Replacement = "新语音" };
        if (kind == RegexReplacementRulesChangeKind.Reordered)
        {
            rules[0] = rule with { Replacement = "中间" };
            rules.Add(SpeechRule("^中间$", "新语音") with { SortOrder = 20 });
        }
        var repository = new PlaybackRegexRepository(rules);
        var workspace = CreateRegexWorkspace(repository);
        workspace.Changed += (_, _) => throw new InvalidOperationException("observer failure");
        var content = CreateRegexContent(repository);
        var audio = new FakeLocalAudioPlaybackCoordinator();
        var generation = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(audio, bookContentService: content, audioProvider: generation, regexWorkspace: workspace);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await WaitForPlayingAsync(coordinator);
        var expectedSpeech = "新语音";
        var revision = coordinator.CurrentSnapshot.ContentRevision;
        switch (kind)
        {
            case RegexReplacementRulesChangeKind.Saved:
                await workspace.SaveEditorAsync(Editor(rule, "新语音"), CancellationToken.None);
                break;
            case RegexReplacementRulesChangeKind.EnabledChanged:
                await workspace.SetRuleEnabledAsync(rule.Id, true, CancellationToken.None);
                break;
            case RegexReplacementRulesChangeKind.Deleted:
                await workspace.DeleteRuleAsync(rule.Id, CancellationToken.None);
                expectedSpeech = "第一段";
                break;
            case RegexReplacementRulesChangeKind.Reordered:
                await workspace.SaveOrderAsync(rules.Select(item => item.Id).Reverse().ToArray(), CancellationToken.None);
                expectedSpeech = "中间";
                break;
            case RegexReplacementRulesChangeKind.Imported:
                await workspace.ImportJsonAsync("""{"name":"新规则","pattern":"^第一段$","replacement":"新语音","scope":"Speech"}""", CancellationToken.None);
                break;
            case RegexReplacementRulesChangeKind.Restored:
                repository.Rules[0] = rule with { Replacement = "新语音" };
                workspace.NotifyConfigurationRestored();
                break;
        }
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.ContentRevision == revision + 1 && coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.Equal(2, generation.Requests.Count);
        Assert.Equal(expectedSpeech, generation.Requests.Last().SpeechText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Speech_change_maps_to_nearest_source_position_even_when_next_speech_is_identical(bool identicalSpeech)
    {
        var rule = SpeechRule("^第一段$", identicalSpeech ? "第二段" : "第一段");
        var repository = new PlaybackRegexRepository([rule]);
        var workspace = CreateRegexWorkspace(repository);
        var generation = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(new FakeLocalAudioPlaybackCoordinator(),
            bookContentService: CreateRegexContent(repository), audioProvider: generation, regexWorkspace: workspace);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        await workspace.SaveEditorAsync(Editor(rule, ""), CancellationToken.None);
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.SegmentIndex == 1 && coordinator.CurrentSnapshot.State == PlaybackState.Playing);
        Assert.Equal(2, generation.Requests.Count);
        Assert.Equal("第二段", generation.Requests.Last().SpeechText);
    }

    [Fact]
    public async Task A_late_regex_projection_cannot_resurrect_an_invalidated_source()
    {
        var repository = new PlaybackRegexRepository([]);
        var workspace = CreateRegexWorkspace(repository);
        var content = CreateRegexContent(repository);
        var changes = new SourceChanges();
        var generation = new FakeAudioGenerationProvider();
        await using var coordinator = CreateCoordinator(new FakeLocalAudioPlaybackCoordinator(),
            bookContentService: content, audioProvider: generation, regexWorkspace: workspace, sourceChanges: changes);
        await coordinator.StartAsync(new("book-1", 0, 0, null, 10), CancellationToken.None);
        var gate = content.BlockNextChapter();
        await workspace.SaveEditorAsync(Editor(SpeechRule("^第一段$", "新语音"), "新语音") with { Id = null }, CancellationToken.None);
        await content.ChapterRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        content.Source.Book = content.Source.Book with { SourceContext = new("source-2", "catalog-2") };
        changes.Publish(new BookCommittedChange.ActiveSourceChanged("book-1", "source-1", "source-2"));
        gate.SetResult();
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookId is null);
        Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
        Assert.Single(generation.Requests);
    }

    private static RegexReplacementRule SpeechRule(string pattern, string replacement) =>
        new(Guid.NewGuid(), "规则", true, 10, pattern, replacement, RegexReplacementScope.Speech, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static RegexReplacementRuleEditorModel Editor(RegexReplacementRule rule, string replacement) =>
        new(rule.Id, rule.Name, rule.Pattern, replacement, rule.Scope);

    private static RegexReplacementRuleWorkspaceService CreateRegexWorkspace(PlaybackRegexRepository repository) =>
        new(repository, new RegexReplacementRuleErrorStore(), TimeProvider.System);

    private static RegexPlaybackContent CreateRegexContent(PlaybackRegexRepository repository) =>
        new(new FakeBookPlaybackContentService(CreateBook() with { SourceContext = new("source-1", "catalog-1") }),
            new RegexReplacementPipeline(repository, new RegexReplacementRuleErrorStore()));

    private sealed class RegexPlaybackContent(FakeBookPlaybackContentService source, IRegexReplacementPipeline pipeline) : IBookPlaybackContentService
    {
        public FakeBookPlaybackContentService Source => source;
        private TaskCompletionSource? _chapterGate;
        public TaskCompletionSource ChapterRequested { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BlockNextChapter()
        {
            ChapterRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _chapterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken) => source.GetBookAsync(bookId, cancellationToken);
        public Task<bool> IsCurrentAsync(PlaybackBookContent book, CancellationToken cancellationToken) => source.IsCurrentAsync(book, cancellationToken);

        public async Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            var chapter = await source.GetChapterAsync(bookId, chapterIndex, cancellationToken);
            if (chapter is null) return null;
            var result = await pipeline.ApplyAsync(chapter.Segments, cancellationToken);
            if (_chapterGate is { } gate)
            {
                _chapterGate = null;
                ChapterRequested.SetResult();
                // Deliberately emulate a content adapter that completes after cancellation.
                await gate.Task;
            }
            return PlaybackChapterContent.FromLoaded(chapterIndex, chapter.Title, result.Segments, chapter.ChapterId);
        }
    }

    private sealed class PlaybackRegexRepository(IEnumerable<RegexReplacementRule> rules) : IRegexReplacementRuleRepository
    {
        public List<RegexReplacementRule> Rules { get; } = rules.ToList();
        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RegexReplacementRule>>(Rules.ToArray());
        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken)
        {
            var index = Rules.FindIndex(item => item.Id == rule.Id);
            if (index < 0) Rules.Add(rule); else Rules[index] = rule;
            return Task.CompletedTask;
        }
        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) =>
            SaveAsync(Rules.Single(rule => rule.Id == ruleId) with { IsEnabled = isEnabled }, cancellationToken);
        public Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken)
        {
            foreach (var (id, sortOrder) in order)
            {
                var index = Rules.FindIndex(rule => rule.Id == id);
                Rules[index] = Rules[index] with { SortOrder = sortOrder };
            }
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken)
        {
            Rules.RemoveAll(rule => rule.Id == ruleId);
            return Task.CompletedTask;
        }
    }
}
