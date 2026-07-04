using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class ChapterRuleWorkspaceServiceTests
{
    [Fact]
    public async Task SaveEditorAsync_deduplicates_rule_names_for_new_rules()
    {
        var repository = new FakeChapterRuleRepository(
        [
            new ChapterRule("custom:existing", "新建规则", @"^\s*旧规则$", 10, true, "now", "now")
        ]);
        var service = new ChapterRuleWorkspaceService(repository, new FakeChapterRuleManagementService());

        var saved = await service.SaveEditorAsync(
            new ChapterRuleEditorModel(null, "新建规则", @"^\s*新规则$", true, false, true),
            CancellationToken.None);

        Assert.Equal("新建规则(2)", saved.Name);
        Assert.Contains(repository.Rules, rule => rule.Name == "新建规则(2)");
    }

    [Fact]
    public async Task SaveEditorAsync_rejects_invalid_regex()
    {
        var service = new ChapterRuleWorkspaceService(
            new FakeChapterRuleRepository([]),
            new FakeChapterRuleManagementService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveEditorAsync(
            new ChapterRuleEditorModel(null, "规则", "[", true, false, true),
            CancellationToken.None));

        Assert.Contains("正则表达式无效", exception.Message);
    }

    [Fact]
    public async Task DeleteRuleAsync_rejects_built_in_rules()
    {
        var service = new ChapterRuleWorkspaceService(
            new FakeChapterRuleRepository([]),
            new FakeChapterRuleManagementService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRuleAsync(
            "builtin:chapter-number",
            CancellationToken.None));
    }

    [Fact]
    public async Task SaveOrderAsync_normalizes_sort_order_to_step_of_ten()
    {
        var repository = new FakeChapterRuleRepository(
        [
            new ChapterRule("a", "A", @"^A$", 100, true, "now", "now"),
            new ChapterRule("b", "B", @"^B$", 200, true, "now", "now"),
            new ChapterRule("c", "C", @"^C$", 300, true, "now", "now")
        ]);
        var service = new ChapterRuleWorkspaceService(repository, new FakeChapterRuleManagementService());

        await service.SaveOrderAsync(["c", "a", "b"], CancellationToken.None);

        Assert.Equal(
        [
            ("c", 10),
            ("a", 20),
            ("b", 30)
        ],
            repository.Rules.OrderBy(rule => rule.SortOrder).Select(rule => (rule.Id, rule.SortOrder)).ToArray());
    }

    private sealed class FakeChapterRuleRepository : IChapterRuleRepository
    {
        public FakeChapterRuleRepository(IReadOnlyList<ChapterRule> rules)
        {
            Rules = rules.ToList();
        }

        public List<ChapterRule> Rules { get; }

        public Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChapterRule>>(Rules.OrderBy(rule => rule.SortOrder).ToArray());
        }

        public Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChapterRule>>(Rules.Where(rule => rule.IsEnabled).ToArray());
        }

        public Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken)
        {
            Rules.RemoveAll(item => item.Id == rule.Id);
            Rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken)
        {
            Rules.RemoveAll(item => item.Id == ruleId);
            return Task.CompletedTask;
        }

        public Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken)
        {
            var existing = Rules.Single(rule => rule.Id == ruleId);
            Rules.RemoveAll(rule => rule.Id == ruleId);
            Rules.Add(existing with { SortOrder = newSortOrder });
            return Task.CompletedTask;
        }

        public Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken)
        {
            foreach (var item in order)
            {
                var existing = Rules.Single(rule => rule.Id == item.RuleId);
                Rules.RemoveAll(rule => rule.Id == item.RuleId);
                Rules.Add(existing with { SortOrder = item.SortOrder });
            }

            return Task.CompletedTask;
        }

        public Task<int> ImportDefaultsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class FakeChapterRuleManagementService : IChapterRuleManagementService
    {
        public Task<ChapterRuleDefaultsPreview> PreviewDefaultsAsync(
            ChapterRuleDefaultsMode mode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ChapterRuleDefaultsPreview(mode, []));
        }

        public Task<ChapterRuleDefaultsApplyResult> ApplyDefaultsAsync(
            ChapterRuleDefaultsMode mode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ChapterRuleDefaultsApplyResult(mode, 0, 0, 0));
        }
    }
}
