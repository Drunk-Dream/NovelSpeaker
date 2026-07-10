using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class RegexReplacementRuleWorkspaceServiceTests
{
    [Fact]
    public async Task SaveEditorAsync_preserves_latest_enabled_state_and_sort_order()
    {
        var id = Guid.NewGuid();
        var repository = new FakeRepository([new RegexReplacementRule(id, "旧名", false, 70, "old", "", RegexReplacementScope.Both, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        var service = new RegexReplacementRuleWorkspaceService(repository);

        await service.SaveEditorAsync(new RegexReplacementRuleEditorModel(id, "新名", "new", "替换", RegexReplacementScope.Speech), CancellationToken.None);

        var saved = Assert.Single(repository.Rules);
        Assert.False(saved.IsEnabled);
        Assert.Equal(70, saved.SortOrder);
        Assert.Equal("新名", saved.Name);
        Assert.Equal(RegexReplacementScope.Speech, saved.Scope);
    }

    [Fact]
    public async Task SaveOrderAsync_normalizes_orders_in_steps_of_ten()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var repository = new FakeRepository([Rule(a, 100), Rule(b, 200)]);
        await new RegexReplacementRuleWorkspaceService(repository).SaveOrderAsync([b, a], CancellationToken.None);
        Assert.Equal([(b, 10), (a, 20)], repository.Rules.OrderBy(rule => rule.SortOrder).Select(rule => (rule.Id, rule.SortOrder)).ToArray());
    }

    [Fact]
    public async Task SaveEditorAsync_rejects_invalid_pattern()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegexReplacementRuleWorkspaceService(new FakeRepository([])).SaveEditorAsync(new RegexReplacementRuleEditorModel(null, "规则", "[", "", RegexReplacementScope.Both), CancellationToken.None));
    }

    private static RegexReplacementRule Rule(Guid id, int order) => new(id, id.ToString(), true, order, "a", "b", RegexReplacementScope.Both, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private sealed class FakeRepository : IRegexReplacementRuleRepository
    {
        public FakeRepository(IReadOnlyList<RegexReplacementRule> rules) => Rules = rules.ToList();
        public List<RegexReplacementRule> Rules { get; }
        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RegexReplacementRule>>(Rules.ToArray());
        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) { var index = Rules.FindIndex(item => item.Id == rule.Id); if (index >= 0) Rules[index] = rule; else Rules.Add(rule); return Task.CompletedTask; }
        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) { var index = Rules.FindIndex(item => item.Id == ruleId); Rules[index] = Rules[index] with { IsEnabled = isEnabled }; return Task.CompletedTask; }
        public Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken) { foreach (var (id, sort) in order) { var index = Rules.FindIndex(item => item.Id == id); Rules[index] = Rules[index] with { SortOrder = sort }; } return Task.CompletedTask; }
        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) { Rules.RemoveAll(item => item.Id == ruleId); return Task.CompletedTask; }
    }
}
