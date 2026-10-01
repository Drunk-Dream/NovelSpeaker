using NovelSpeaker.App.Features.Rules.Metadata;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.Domain.Books;
using System.Text.Json;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class MetadataRuleWorkbenchViewModelTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public async Task Metadata_bulk_selection_and_exchange_preserve_drafts_and_isolate_invalid_items(bool header, int count)
    {
        var repository = TwoRules();
        repository.Rules.Add(new FileNameMetadataRule("third", "三", @"(?<description>三)", 30, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        var documents = new FakeRuleDocumentInteraction();
        MetadataRuleWorkbenchViewModel CreateWorkbench(FileRules rules) => header
            ? new TextHeaderMetadataRulesViewModel(rules, new Navigator(), new Dialogs(), new Feedback(), documents)
            : Create(rules, documents);
        var vm = CreateWorkbench(repository);
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectRuleWithModifiersAsync(vm.Rules[0], DesktopSelectionModifiers.None, CancellationToken.None);
        vm.DraftName = "Unsaved";
        await vm.SelectRuleWithModifiersAsync(vm.Rules[2], DesktopSelectionModifiers.Control, CancellationToken.None);
        await vm.SelectRuleWithModifiersAsync(vm.Rules[2], DesktopSelectionModifiers.Control, CancellationToken.None);
        Assert.Equal("first", Assert.Single(vm.Rules, row => row.IsSelected).Id);
        await vm.SelectRuleWithModifiersAsync(vm.Rules[0], DesktopSelectionModifiers.None, CancellationToken.None);
        if (count > 1)
            await vm.SelectRuleWithModifiersAsync(vm.Rules[2], DesktopSelectionModifiers.Shift, CancellationToken.None);
        await vm.ExportRuleCommand.ExecuteAsync(vm.Rules[0]);
        using var exported = JsonDocument.Parse(documents.ExportedJson!);
        var entries = exported.RootElement.GetProperty("rules").EnumerateArray().ToArray();
        Assert.Equal(count, entries.Length);
        Assert.Equal(repository.Rules.Take(count).Select(rule => rule.Name), entries.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal("Unsaved", vm.DraftName);
        Assert.True(vm.HasUnsavedChanges);
        var target = new FileRules();
        var receiving = CreateWorkbench(target);
        await receiving.LoadAsync(CancellationToken.None);
        documents.ClipboardDocument = new RuleImportDocument(documents.ExportedJson!, "fixture");
        await receiving.ImportClipboardCommand.ExecuteAsync(null);
        Assert.Equal(count, target.Rules.Count);
        Assert.False(receiving.HasEditor);
        await receiving.ImportClipboardCommand.ExecuteAsync(null);
        Assert.Equal(count, target.Rules.Count);
        // Malformed item between valid items must not interrupt either side.
        documents.ClipboardDocument = new RuleImportDocument(System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            ruleType = header ? "textHeaderMetadata" : "fileNameMetadata",
            rules = new object[] { new { name = "A", pattern = "(?<author>A)" }, 42, new { name = "B", pattern = "(?<author>B)" } }
        }), "fixture");
        await vm.ImportClipboardCommand.ExecuteAsync(null);
        Assert.Equal(5, repository.Rules.Count);
        Assert.Equal("Unsaved", vm.DraftName);
        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task File_name_workbench_validates_named_capture_before_saving()
    {
        var repository = new FileRules();
        var viewModel = Create(repository);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.NewRuleCommand.ExecuteAsync(null);
        viewModel.DraftName = "文件名";
        viewModel.DraftPattern = "^.+$";

        await viewModel.SaveRuleCommand.ExecuteAsync(null);
        Assert.Empty(repository.Rules);
        Assert.NotEmpty(viewModel.ValidationMessage);

        viewModel.DraftPattern = @"^(?<name>.+)$";
        await viewModel.SaveRuleCommand.ExecuteAsync(null);
        Assert.Single(repository.Rules);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.True(Assert.Single(viewModel.Rules).IsSelected);
    }

    [Fact]
    public async Task Import_skips_invalid_items_and_preserves_current_editor_draft()
    {
        var now = DateTimeOffset.UnixEpoch;
        var repository = new FileRules();
        repository.Rules.Add(new FileNameMetadataRule("existing", "现有", @"(?<name>.+)", 10, true, now, now));
        var documents = new FakeRuleDocumentInteraction
        {
            ClipboardDocument = new RuleImportDocument(
                """{"schemaVersion":1,"ruleType":"fileNameMetadata","rules":[{}, {"name":"缺模式"}, {"name":"无效","pattern":".*"}, {"name":"现有","pattern":"(?<author>.+)"}]}""",
                "剪贴板")
        };
        var viewModel = Create(repository, documents);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[0]);
        viewModel.DraftPattern = @"(?<description>尚未保存)";

        await viewModel.ImportClipboardCommand.ExecuteAsync(null);

        Assert.Equal(2, repository.Rules.Count);
        Assert.Equal("现有(2)", repository.Rules.Single(rule => rule.Id != "existing").Name);
        Assert.Equal(@"(?<description>尚未保存)", viewModel.DraftPattern);
        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task Saving_draft_before_toggle_keeps_new_pattern()
    {
        var repository = TwoRules();
        var dialogs = new Dialogs { UnsavedDecision = UnsavedChangesDecision.Save };
        var viewModel = Create(repository, dialogs: dialogs);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[0]);
        viewModel.DraftPattern = @"(?<author>更新)";

        await viewModel.ToggleRuleEnabledCommand.ExecuteAsync(viewModel.Rules[0]);

        Assert.Equal(@"(?<author>更新)", repository.Rules.Single(rule => rule.Id == "first").Pattern);
        Assert.False(repository.Rules.Single(rule => rule.Id == "first").IsEnabled);
        Assert.True(viewModel.Rules.Single(rule => rule.Id == "first").IsSelected);
    }

    [Fact]
    public async Task Saving_draft_before_move_still_reorders_rule()
    {
        var repository = TwoRules();
        var dialogs = new Dialogs { UnsavedDecision = UnsavedChangesDecision.Save };
        var viewModel = Create(repository, dialogs: dialogs);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[0]);
        viewModel.DraftPattern = @"(?<author>更新)";

        await viewModel.MoveRuleDownCommand.ExecuteAsync(viewModel.Rules[0]);

        Assert.Equal("second", viewModel.Rules[0].Id);
        Assert.Equal("first", viewModel.Rules[1].Id);
        Assert.Equal(@"(?<author>更新)", repository.Rules.Single(rule => rule.Id == "first").Pattern);
        Assert.True(viewModel.Rules.Single(rule => rule.Id == "first").IsSelected);
    }

    [Fact]
    public async Task Selected_rules_export_in_visible_order_with_rule_type()
    {
        var repository = TwoRules();
        var documents = new FakeRuleDocumentInteraction();
        var viewModel = Create(repository, documents);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectRuleWithModifiersAsync(viewModel.Rules[1], DesktopSelectionModifiers.None, CancellationToken.None);
        await viewModel.SelectRuleWithModifiersAsync(viewModel.Rules[0], DesktopSelectionModifiers.Control, CancellationToken.None);

        await viewModel.ExportRuleCommand.ExecuteAsync(viewModel.Rules[1]);

        using var json = JsonDocument.Parse(Assert.IsType<string>(documents.ExportedJson));
        Assert.Equal("fileNameMetadata", json.RootElement.GetProperty("ruleType").GetString());
        Assert.Equal(new[] { "一", "二" }, json.RootElement.GetProperty("rules").EnumerateArray()
            .Select(rule => rule.GetProperty("name").GetString()).ToArray());
        Assert.All(viewModel.Rules, row => Assert.True(row.IsSelected));
    }

    [Fact]
    public async Task Import_rejects_document_for_other_rule_type()
    {
        var repository = new FileRules();
        var feedback = new Feedback();
        var documents = new FakeRuleDocumentInteraction
        {
            ClipboardDocument = new RuleImportDocument(
                """{"schemaVersion":1,"ruleType":"textHeaderMetadata","rules":[{"name":"正文","pattern":"(?<name>.+)"}]}""",
                "剪贴板")
        };
        var viewModel = Create(repository, documents, feedback: feedback);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ImportClipboardCommand.ExecuteAsync(null);

        Assert.Empty(repository.Rules);
        Assert.Equal(1, feedback.ErrorCount);
    }

    private static FileRules TwoRules()
    {
        var now = DateTimeOffset.UnixEpoch;
        var repository = new FileRules();
        repository.Rules.Add(new FileNameMetadataRule("first", "一", @"(?<name>一)", 10, true, now, now));
        repository.Rules.Add(new FileNameMetadataRule("second", "二", @"(?<name>二)", 20, true, now, now));
        return repository;
    }

    private static FileNameMetadataRulesViewModel Create(FileRules repository, IRuleDocumentInteraction? documents = null,
        Dialogs? dialogs = null, Feedback? feedback = null) =>
        new(repository, new Navigator(), dialogs ?? new Dialogs(), feedback ?? new Feedback(),
            documents ?? new FakeRuleDocumentInteraction());

    private sealed class FileRules : NovelSpeaker.Application.Books.IFileNameMetadataRuleRepository,
        NovelSpeaker.Application.Books.ITextHeaderMetadataRuleRepository
    {
        public List<FileNameMetadataRule> Rules { get; } = [];
        public Task<IReadOnlyList<FileNameMetadataRule>> GetAllAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<FileNameMetadataRule>>(Rules.ToArray());
        Task<IReadOnlyList<TextHeaderMetadataRule>> NovelSpeaker.Application.Books.ITextHeaderMetadataRuleRepository.GetAllAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<TextHeaderMetadataRule>>(Rules.Select(rule => new TextHeaderMetadataRule(
                rule.Id, rule.Name, rule.Pattern, rule.SortOrder, rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt)).ToArray());
        public Task SaveAsync(TextHeaderMetadataRule rule, CancellationToken token) =>
            SaveAsync(new FileNameMetadataRule(rule.Id, rule.Name, rule.Pattern, rule.SortOrder,
                rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt), token);
        public Task SaveAsync(FileNameMetadataRule rule, CancellationToken token)
        {
            Rules.RemoveAll(candidate => candidate.Id == rule.Id);
            Rules.Add(rule);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string id, CancellationToken token)
        {
            Rules.RemoveAll(rule => rule.Id == id);
            return Task.CompletedTask;
        }
        public Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken token)
        {
            foreach (var (id, sortOrder) in order)
            {
                var index = Rules.FindIndex(rule => rule.Id == id);
                Rules[index] = Rules[index] with { SortOrder = sortOrder };
            }
            return Task.CompletedTask;
        }
    }

    private sealed class Navigator : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.Settings;
        public Task<bool> NavigateAsync(AppRoute route, CancellationToken token, bool bypassGuard = false) => Task.FromResult(true);
        public Task<bool> NavigateBackAsync(CancellationToken token, bool bypassGuard = false) => Task.FromResult(true);
    }

    private sealed class Dialogs : IAppDialogService
    {
        public UnsavedChangesDecision UnsavedDecision { get; set; } = UnsavedChangesDecision.Discard;
        public Task<AppConfirmationDecision> ShowConfirmationAsync(string title, string message, string primary, string close, CancellationToken token) =>
            Task.FromResult(AppConfirmationDecision.Confirm);
        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(string title, string message, string save, string discard, string cancel, CancellationToken token) =>
            Task.FromResult(UnsavedDecision);
    }

    private sealed class Feedback : IAppFeedbackService
    {
        public int ErrorCount { get; private set; }
        public ProjectedUiError Project(Exception exception) => new ExceptionProjector().Project(exception);
        public void ShowProjectedNotification(string title, ProjectedUiError projected) => ErrorCount++;
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken token) =>
            Task.FromResult(AppConfirmationDecision.Confirm);
    }
}
