using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class ChapterRulesViewModelTests
{
    [Fact]
    public async Task ImportDefaultsAsync_refreshes_rule_rows_and_sets_status()
    {
        var repository = new FakeChapterRuleRepository([
            new ChapterRule("1", "章节数字", @"^\s*第[0-9]+章.*$", 10, true, "now", "now")
        ]);
        var feedback = new FakeFeedbackService();

        var viewModel = new ChapterRulesViewModel(repository, feedback);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.ImportDefaultsAsync(CancellationToken.None);

        Assert.True(viewModel.Rules.Count >= 1);
        Assert.All(viewModel.Rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Pattern)));
        Assert.Equal("默认规则已导入。", viewModel.StatusMessage);
        Assert.Equal("默认规则导入完成", feedback.LastTitle);
    }

    [Fact]
    public async Task DeleteRuleAsync_requires_confirmation_before_deleting()
    {
        var repository = new FakeChapterRuleRepository([
            new ChapterRule("1", "章节数字", @"^\s*第[0-9]+章.*$", 10, true, "now", "now")
        ]);
        var feedback = new FakeFeedbackService
        {
            NextConfirmationDecision = AppConfirmationDecision.Cancel
        };
        var viewModel = new ChapterRulesViewModel(repository, feedback);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.DeleteRuleAsync("1", CancellationToken.None);

        Assert.Single(viewModel.Rules);
        Assert.Equal("已取消删除规则：章节数字", viewModel.StatusMessage);

        feedback.NextConfirmationDecision = AppConfirmationDecision.Confirm;
        await viewModel.DeleteRuleAsync("1", CancellationToken.None);

        Assert.Empty(viewModel.Rules);
        Assert.Equal("删除章节规则", feedback.LastConfirmationTitle);
    }

    private sealed class FakeChapterRuleRepository : IChapterRuleRepository
    {
        private readonly List<ChapterRule> _rules;

        public FakeChapterRuleRepository(IReadOnlyList<ChapterRule> rules)
        {
            _rules = rules.ToList();
        }

        public Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChapterRule>>(_rules.ToArray());
        }

        public Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChapterRule>>(_rules.Where(rule => rule.IsEnabled).ToArray());
        }

        public Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken)
        {
            _rules.RemoveAll(item => item.Id == rule.Id);
            _rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken)
        {
            _rules.RemoveAll(item => item.Id == ruleId);
            return Task.CompletedTask;
        }

        public Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken)
        {
            var index = _rules.FindIndex(item => item.Id == ruleId);
            if (index >= 0)
            {
                _rules[index] = _rules[index] with { SortOrder = newSortOrder };
            }

            return Task.CompletedTask;
        }

        public Task<int> ImportDefaultsAsync(CancellationToken cancellationToken)
        {
            if (_rules.All(rule => rule.Name != "默认规则"))
            {
                _rules.Add(new ChapterRule("default-1", "默认规则", @"^\s*第.+$", 99, true, "now", "now"));
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public string? LastConfirmationTitle { get; private set; }

        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Confirm;

        public ProjectedUiError Project(Exception exception)
        {
            return new ExceptionProjector().Project(exception);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastTitle = title;
        }

        public void ShowSuccess(string title, string message)
        {
            LastTitle = title;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            LastConfirmationTitle = title;
            return Task.FromResult(NextConfirmationDecision);
        }
    }
}
