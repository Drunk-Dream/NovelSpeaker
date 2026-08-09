using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.ChapterRules;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

public sealed class ChapterRuleWorkspaceServiceTests
{
    [Fact]
    public async Task GetRulesAsync_projects_delete_capability_without_changing_list_identity()
    {
        var service = new ChapterRuleWorkspaceService(
            new FakeChapterRuleRepository(
            [
                new ChapterRule("builtin:chapter-number", "默认规则", @"^默认$", 10, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                new ChapterRule("custom:one", "自定义规则", @"^自定义$", 20, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            ]),
            new FakeChapterRuleManagementService(),
            TimeProvider.System);

        var rules = await service.GetRulesAsync(CancellationToken.None);

        Assert.False(rules.Single(rule => rule.Id == "builtin:chapter-number").CanDelete);
        Assert.True(rules.Single(rule => rule.Id == "custom:one").CanDelete);
    }

    [Fact]
    public async Task SaveEditorAsync_deduplicates_rule_names_for_new_rules()
    {
        var repository = new FakeChapterRuleRepository(
        [
            new ChapterRule("custom:existing", "新建规则", @"^\s*旧规则$", 10, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        ]);
        var service = new ChapterRuleWorkspaceService(
            repository,
            new FakeChapterRuleManagementService(),
            TimeProvider.System);

        var saved = await service.SaveEditorAsync(
            new ChapterRuleEditorModel(null, "新建规则", @"^\s*新规则$", false, true),
            CancellationToken.None);

        Assert.Equal("新建规则(2)", saved.Name);
        Assert.Contains(repository.Rules, rule => rule.Name == "新建规则(2)");
    }

    [Fact]
    public async Task SaveEditorAsync_rejects_invalid_regex()
    {
        var service = new ChapterRuleWorkspaceService(
            new FakeChapterRuleRepository([]),
            new FakeChapterRuleManagementService(),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveEditorAsync(
            new ChapterRuleEditorModel(null, "规则", "[", false, true),
            CancellationToken.None));

        Assert.Contains("正则表达式无效", exception.Message);
    }

    [Fact]
    public async Task DeleteRuleAsync_rejects_built_in_rules()
    {
        var service = new ChapterRuleWorkspaceService(
            new FakeChapterRuleRepository([]),
            new FakeChapterRuleManagementService(),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRuleAsync(
            "builtin:chapter-number",
            CancellationToken.None));
    }

    [Fact]
    public async Task SaveOrderAsync_normalizes_sort_order_to_step_of_ten()
    {
        var repository = new FakeChapterRuleRepository(
        [
            new ChapterRule("a", "A", @"^A$", 100, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            new ChapterRule("b", "B", @"^B$", 200, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            new ChapterRule("c", "C", @"^C$", 300, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        ]);
        var service = new ChapterRuleWorkspaceService(
            repository,
            new FakeChapterRuleManagementService(),
            TimeProvider.System);

        await service.SaveOrderAsync(["c", "a", "b"], CancellationToken.None);

        Assert.Equal(
        [
            ("c", 10),
            ("a", 20),
            ("b", 30)
        ],
            repository.Rules.OrderBy(rule => rule.SortOrder).Select(rule => (rule.Id, rule.SortOrder)).ToArray());
    }

    [Fact]
    public async Task SaveEditorAsync_existing_rule_preserves_latest_enabled_state_and_sort_order()
    {
        var repository = new FakeChapterRuleRepository(
        [
            new ChapterRule("custom:one", "规则一", @"^\s*一$", 70, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        ]);
        var service = new ChapterRuleWorkspaceService(
            repository,
            new FakeChapterRuleManagementService(),
            TimeProvider.System);

        var saved = await service.SaveEditorAsync(
            new ChapterRuleEditorModel("custom:one", "规则一-已改", @"^\s*已改$", false, true),
            CancellationToken.None);

        var persisted = Assert.Single(repository.Rules);
        Assert.Equal("规则一-已改", saved.Name);
        Assert.Equal("规则一-已改", persisted.Name);
        Assert.Equal(@"^\s*已改$", persisted.Pattern);
        Assert.False(persisted.IsEnabled);
        Assert.Equal(70, persisted.SortOrder);
    }

    [Fact]
    public async Task Json_export_and_array_import_preserve_state_ignore_ids_and_append_in_source_order()
    {
        var repository = new FakeChapterRuleRepository(
        [
            new ChapterRule("custom:existing", "同名", "^旧$", 40, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        ]);
        var service = new ChapterRuleWorkspaceService(repository, new FakeChapterRuleManagementService(), TimeProvider.System);

        var exported = await service.ExportRuleJsonAsync("custom:existing", CancellationToken.None);
        var result = await service.ImportJsonAsync(
            """
            [
              {"id":"custom:existing","name":"同名","pattern":"^旧$","isEnabled":false},
              {"id":"custom:existing","name":"同名","pattern":"^新一$","isEnabled":true},
              {"name":"第二条","pattern":"^新二$","isEnabled":false}
            ]
            """,
            CancellationToken.None);

        Assert.Equal("""{"name":"同名","pattern":"^旧$","isEnabled":false}""", exported);
        Assert.Equal(new RuleJsonImportResult(2, 1, 3), result);
        var ordered = repository.Rules.OrderBy(rule => rule.SortOrder).ToArray();
        Assert.Equal(["custom:existing", ordered[1].Id, ordered[2].Id], ordered.Select(rule => rule.Id));
        Assert.All(ordered.Skip(1), rule => Assert.StartsWith("custom:", rule.Id, StringComparison.Ordinal));
        Assert.Equal(["^旧$", "^新一$", "^新二$"], ordered.Select(rule => rule.Pattern));
        Assert.Equal([false, true, false], ordered.Select(rule => rule.IsEnabled));
    }

    [Fact]
    public async Task Json_import_validates_entire_source_before_writing()
    {
        var repository = new FakeChapterRuleRepository([]);
        var service = new ChapterRuleWorkspaceService(repository, new FakeChapterRuleManagementService(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportJsonAsync(
            """[{"name":"有效","pattern":"^ok$"},{"name":"无效","pattern":"["}]""",
            CancellationToken.None));

        Assert.Empty(repository.Rules);
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
