using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

public sealed class RegexReplacementRuleWorkspaceServiceTests
{
    [Fact]
    public async Task SaveEditorAsync_preserves_latest_enabled_state_and_sort_order()
    {
        var id = Guid.NewGuid();
        var repository = new FakeRepository([new RegexReplacementRule(id, "旧名", false, 70, "old", "", RegexReplacementScope.Both, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        var service = CreateService(repository);

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
        await CreateService(repository).SaveOrderAsync([b, a], CancellationToken.None);
        Assert.Equal([(b, 10), (a, 20)], repository.Rules.OrderBy(rule => rule.SortOrder).Select(rule => (rule.Id, rule.SortOrder)).ToArray());
    }

    [Fact]
    public async Task SaveEditorAsync_rejects_invalid_pattern()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(new FakeRepository([])).SaveEditorAsync(new RegexReplacementRuleEditorModel(null, "规则", "[", "", RegexReplacementScope.Both), CancellationToken.None));
    }

    [Fact]
    public async Task GetRulesAsync_marks_malformed_historical_pattern_without_losing_other_rules()
    {
        var malformed = Rule(Guid.NewGuid(), 10) with { Pattern = "[" };
        var valid = Rule(Guid.NewGuid(), 20);
        var service = CreateService(new FakeRepository([malformed, valid]));

        var rules = await service.GetRulesAsync(CancellationToken.None);

        Assert.Equal(2, rules.Count);
        Assert.Equal("规则格式无效，已隔离。", rules.Single(rule => rule.Id == malformed.Id).ErrorMessage);
        Assert.Null(rules.Single(rule => rule.Id == valid.Id).ErrorMessage);
    }

    [Fact]
    public async Task GetRulesAsync_propagates_cancellation_to_repository()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new CancellationAwareRepository();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(repository).GetRulesAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, repository.ObservedToken);
    }

    [Fact]
    public async Task Json_export_and_array_import_preserve_fields_ignore_ids_and_append_in_source_order()
    {
        var existingId = Guid.NewGuid();
        var repository = new FakeRepository(
        [
            new RegexReplacementRule(existingId, "同名", false, 40, "旧", "甲", RegexReplacementScope.Display, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        ]);
        var service = CreateService(repository);

        var exported = await service.ExportRuleJsonAsync(existingId, CancellationToken.None);
        var result = await service.ImportJsonAsync(
            $$"""
            [
              {"id":"{{existingId}}","name":"同名","pattern":"旧","replacement":"甲","scope":"Display","isEnabled":false},
              {"id":"{{existingId}}","name":"同名","pattern":"新一","replacement":"乙","scope":"Speech","isEnabled":true},
              {"name":"第二条","pattern":"新二","replacement":"丙","scope":"Both","isEnabled":false}
            ]
            """,
            CancellationToken.None);

        Assert.Equal("""{"name":"同名","pattern":"旧","replacement":"甲","scope":"Display","isEnabled":false}""", exported);
        Assert.Equal(new RuleJsonImportResult(2, 1, 3), result);
        var ordered = repository.Rules.OrderBy(rule => rule.SortOrder).ToArray();
        Assert.Equal(existingId, ordered[0].Id);
        Assert.NotEqual(existingId, ordered[1].Id);
        Assert.NotEqual(existingId, ordered[2].Id);
        Assert.Equal(["旧", "新一", "新二"], ordered.Select(rule => rule.Pattern));
        Assert.Equal([RegexReplacementScope.Display, RegexReplacementScope.Speech, RegexReplacementScope.Both], ordered.Select(rule => rule.Scope));
        Assert.Equal([false, true, false], ordered.Select(rule => rule.IsEnabled));
    }

    [Fact]
    public async Task Json_import_validates_entire_source_before_writing()
    {
        var repository = new FakeRepository([]);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportJsonAsync(
            """[{"name":"有效","pattern":"ok","scope":"Both"},{"name":"无效","pattern":"[","scope":"Both"}]""",
            CancellationToken.None));

        Assert.Empty(repository.Rules);
    }

    private static RegexReplacementRuleWorkspaceService CreateService(IRegexReplacementRuleRepository repository)
    {
        return new RegexReplacementRuleWorkspaceService(
            repository,
            new RegexReplacementRuleErrorStore(),
            TimeProvider.System);
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

    private sealed class CancellationAwareRepository : IRegexReplacementRuleRepository
    {
        public CancellationToken ObservedToken { get; private set; }

        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            return Task.FromCanceled<IReadOnlyList<RegexReplacementRule>>(cancellationToken);
        }

        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
