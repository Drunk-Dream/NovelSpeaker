using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Features.ExperimentalFeatures;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.PresentationTests.TestDoubles;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class SpeechServicesViewModelTests
{
    [Fact]
    public async Task Management_export_skips_edge_preserves_editor_and_requires_credentials_warning()
    {
        var fixture = new Fixture();
        var edge = await fixture.EnableEdgeAsync(null);
        var second = CreateProvider("Second", 10);
        fixture.Store.Items.Add(second);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderWithModifiersAsync(vm.Providers[0], DesktopSelectionModifiers.None, CancellationToken.None);
        vm.DraftName = "Unsaved";
        fixture.Dialogs.NextUnsavedDecision = UnsavedChangesDecision.Cancel;
        await vm.SelectProviderWithModifiersAsync(vm.Providers.Single(item => item.Id == second.Id), DesktopSelectionModifiers.Control, CancellationToken.None);
        Assert.False(vm.IsManagementMode);
        Assert.Equal("Unsaved", vm.DraftName);
        fixture.Dialogs.NextUnsavedDecision = UnsavedChangesDecision.Discard;
        await vm.EnterManagementCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(3, vm.SelectedCount);
        Assert.Equal(fixture.First.Id, vm.SelectedProviderId);
        vm.DraftName = "Unsaved";
        await vm.ExportProviderCommand.ExecuteAsync(vm.Providers[0]);
        Assert.Null(fixture.Documents.ExportedJson);
        fixture.Dialogs.NextConfirmationDecision = AppConfirmationDecision.Confirm;
        await vm.ExportProviderCommand.ExecuteAsync(vm.Providers.Single(item => item.Id == second.Id));
        var exported = ProviderEnvelopeCodec.Read(fixture.Documents.ExportedJson!);
        Assert.Equal(["First", "Second"], exported.Items.Select(item => item.Name));
        Assert.Equal("成功 2，跳过 1，失败 0。", fixture.Feedback.LastMessage);
        Assert.True(fixture.Feedback.LastWasWarning);
        Assert.Equal(3, vm.SelectedCount);
        await vm.SelectProviderWithModifiersAsync(vm.Providers.Single(item => item.Id == second.Id), DesktopSelectionModifiers.None, CancellationToken.None);
        Assert.Equal(fixture.First.Id, vm.SelectedProviderId);
        vm.CancelManagementCommand.Execute(null);
        Assert.Equal(fixture.First.Id, Assert.Single(vm.Providers, item => item.IsSelected).Id);
        fixture.Dialogs.NextUnsavedDecision = UnsavedChangesDecision.Cancel;
        fixture.Documents.ClipboardDocument = new RuleImportDocument(
            ProviderEnvelopeCodec.Write(CreateProvider("Imported", 0)), "fixture");
        await vm.ImportProvidersFromClipboardCommand.ExecuteAsync(null);
        Assert.Contains(vm.Providers, item => item.Name == "Imported");
        Assert.Equal("Unsaved", vm.DraftName);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal(fixture.First.Id, vm.SelectedProviderId);
        Assert.Equal(fixture.First.Id, fixture.Settings.Current.CurrentProviderId);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Batch_delete_continues_after_failure_and_clears_current_provider_without_fallback()
    {
        var fixture = new Fixture();
        var failed = CreateProvider("Failed", -10);
        fixture.Store.Items.Add(failed);
        fixture.Store.FailedDeleteId = failed.Id;
        await fixture.EnableEdgeAsync(null);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers.Single(item => item.Id == fixture.First.Id));
        await vm.EnterManagementCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);
        await vm.DeleteProviderCommand.ExecuteAsync(null);
        Assert.Null(fixture.Settings.Current.CurrentProviderId);
        Assert.False(vm.HasEditor);
        Assert.Equal(2, vm.Providers.Count);
        Assert.Contains(vm.Providers, item => item.Id == failed.Id);
        Assert.DoesNotContain(vm.Providers, item => item.Id == fixture.First.Id);
        Assert.Equal(1, fixture.Feedback.DeletionPromptCount);
        Assert.Equal("成功 1，跳过 1，失败 1。", fixture.Feedback.LastMessage);
        Assert.True(fixture.Feedback.LastWasWarning);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Visibility_change_removes_hidden_provider_from_batch_selection()
    {
        var fixture = new Fixture();
        var edge = await fixture.EnableEdgeAsync(null);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.EnterManagementCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(2, vm.SelectedCount);
        await fixture.Settings.UpdateAsync(new AppSettingsUpdate
        {
            ExperimentalFeatureChange = new ExperimentalFeatureChange(ExperimentalFeaturesService.MicrosoftEdgeTts, false)
        }, CancellationToken.None);
        Assert.DoesNotContain(vm.Providers, item => item.Id == edge.Id);
        Assert.Equal(1, vm.SelectedCount);
        await vm.DeleteProviderCommand.ExecuteAsync(null);
        Assert.Single(fixture.Store.Items, item => item.Id == edge.Id);
        Assert.Empty(vm.Providers);
        Assert.True(vm.IsManagementMode);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Draft_save_preserves_headers_and_structured_rate_without_changing_current_provider()
    {
        var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.NewProviderCommand.ExecuteAsync(null);
        Assert.Single(vm.Providers);
        Assert.True(vm.IsEditingNewProvider);
        vm.DraftName = "New provider";
        vm.DraftUrl = "https://example.invalid/audio";
        vm.DraftRequestMethod = "POST";
        vm.DraftRequestBody = "{}";
        vm.DraftMaxRequests = "2/1000";
        vm.DraftWindowMilliseconds = "1000";
        vm.AddHeaderEntryCommand.Execute(null);
        vm.HeaderEntries[0].Key = "Content-Type";
        vm.HeaderEntries[0].Value = "application/json";
        await vm.SaveDraftCommand.ExecuteAsync(null);
        Assert.Single(vm.Providers);
        Assert.True(vm.HasUnsavedChanges);
        vm.DraftMaxRequests = "2";
        await vm.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Providers.Count);
        Assert.False(vm.IsEditingNewProvider);
        Assert.False(vm.HasUnsavedChanges);
        var saved = fixture.Store.Items.Single(provider => provider.Name == "New provider");
        var configuration = Assert.IsType<HttpSpeechProviderConfiguration>(saved.Configuration);
        Assert.Equal(new ProviderRequestRateLimit(2, 1000), configuration.RateLimit);
        Assert.Equal("application/json", configuration.Headers["Content-Type"]);
        Assert.Equal("{}", configuration.BodyTemplate);
        Assert.Equal(fixture.First.Id, fixture.Settings.Current.CurrentProviderId);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers.Single(provider => provider.Id == fixture.First.Id));
        Assert.Equal(fixture.First.Id, fixture.Settings.Current.CurrentProviderId);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Theory]
    [InlineData(UnsavedChangesDecision.Save)]
    [InlineData(UnsavedChangesDecision.Discard)]
    [InlineData(UnsavedChangesDecision.Cancel)]
    public async Task Dirty_selection_honors_save_discard_and_cancel(UnsavedChangesDecision decision)
    {
        var fixture = new Fixture();
        var second = CreateProvider("Second", 1);
        fixture.Store.Items.Add(second);
        fixture.Dialogs.NextUnsavedDecision = decision;
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers.Single(provider => provider.Id == fixture.First.Id));
        vm.DraftName = "Edited";
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers.Single(provider => provider.Id == second.Id));
        Assert.Equal(decision == UnsavedChangesDecision.Cancel ? fixture.First.Id : second.Id, vm.SelectedProviderId);
        Assert.Equal(decision == UnsavedChangesDecision.Save ? "Edited" : "First", fixture.Store.Items.Single(provider => provider.Id == fixture.First.Id).Name);
        Assert.Equal(fixture.First.Id, fixture.Settings.Current.CurrentProviderId);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Provider_management_reorders_persisted_list_and_deletes_current_without_fallback()
    {
        var fixture = new Fixture();
        var second = CreateProvider("Second", 1);
        fixture.Store.Items.Add(second);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers[0]);
        vm.DraftName = "Dirty";
        await vm.ReorderProviderCommand.ExecuteAsync(new RuleReorderRequest(vm.Providers[0], 2));
        Assert.Equal(["Second", "First"], vm.Providers.Select(provider => provider.Name));
        Assert.True(vm.HasUnsavedChanges);
        await vm.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Store.Items.Single(provider => provider.Id == fixture.First.Id).SortOrder);
        await vm.DeleteProviderCommand.ExecuteAsync(vm.Providers.Single(provider => provider.Id == fixture.First.Id));
        Assert.Null(fixture.Settings.Current.CurrentProviderId);
        Assert.Equal(second.Id, Assert.Single(vm.Providers).Id);
        Assert.False(vm.HasEditor);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Export_requires_confirmation_and_import_uses_provider_envelope()
    {
        var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.ExportProviderToClipboardCommand.ExecuteAsync(vm.Providers[0]);
        Assert.Null(fixture.Documents.CopiedJson);
        fixture.Dialogs.NextConfirmationDecision = AppConfirmationDecision.Confirm;
        await vm.ExportProviderToClipboardCommand.ExecuteAsync(vm.Providers[0]);
        Assert.True(ProviderEnvelopeCodec.Read(fixture.Documents.CopiedJson!).IsValid);
        fixture.Documents.FileDocument = new RuleImportDocument(ProviderEnvelopeCodec.Write(CreateProvider("Imported", 0)), "fixture.json");
        await vm.ImportProvidersCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Providers.Count);
        Assert.Equal(fixture.First.Id, fixture.Settings.Current.CurrentProviderId);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Copy_creates_unique_identity_and_name_immediately_after_source_without_changing_selection()
    {
        var fixture = new Fixture();
        var second = CreateProvider("Second", 1);
        fixture.Store.Items.Add(second);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.CopyProviderCommand.ExecuteAsync(vm.Providers[0]);
        var copy = fixture.Store.Items.Single(provider => provider.Name == "First (副本)");
        Assert.NotEqual(fixture.First.Id, copy.Id);
        Assert.Equal([fixture.First.Id, copy.Id, second.Id], vm.Providers.Select(provider => provider.Id));
        await vm.CopyProviderCommand.ExecuteAsync(vm.Providers[0]);
        Assert.Equal(4, vm.Providers.Select(provider => provider.Id).Distinct().Count());
        Assert.Equal(4, vm.Providers.Select(provider => provider.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(HttpProviderConfigurationComparer.IsSamePortableProvider("same", (HttpSpeechProviderConfiguration)fixture.First.Configuration,
            "same", (HttpSpeechProviderConfiguration)copy.Configuration));
        Assert.Equal(fixture.First.Id, fixture.Settings.Current.CurrentProviderId);
        Assert.Null(fixture.Documents.CopiedJson);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Dirty_save_failure_preserves_draft_and_blocks_navigation()
    {
        var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers[0]);
        vm.DraftName = "Dirty";
        fixture.Dialogs.NextUnsavedDecision = UnsavedChangesDecision.Save;
        fixture.Store.SaveFailure = new System.IO.IOException("fixture failure");
        Assert.False(await vm.ConfirmLeaveAsync(CancellationToken.None));
        await vm.BackCommand.ExecuteAsync(null);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal("Dirty", vm.DraftName);
        Assert.Equal("First", Assert.Single(fixture.Store.Items).Name);
        Assert.Equal(fixture.First.Id, vm.SelectedProviderId);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Page_leave_cancels_draft_synthesis_and_stops_preview()
    {
        var fixture = new Fixture();
        fixture.Runtime.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers[0]);
        vm.DraftUrl = "https://example.invalid/unsaved";
        var audition = vm.TestDraftCommand.ExecuteAsync(null);
        await fixture.Runtime.Entered.Task;
        Assert.Equal(vm.DraftUrl, Assert.IsType<HttpSpeechProviderConfiguration>(fixture.Runtime.Provider!.Configuration).UrlTemplate);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
        await audition;
        Assert.False(vm.IsTestBusy);
        Assert.False(fixture.Player.IsPlaying);
        Assert.Equal(fixture.First.Id, fixture.Settings.Current.CurrentProviderId);
    }

    [Fact]
    public async Task Edge_voice_selection_and_preview_use_draft_until_save_without_auto_selection()
    {
        var fixture = new Fixture();
        var edge = await fixture.EnableEdgeAsync(null);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers.Single(item => item.Id == edge.Id));
        await WaitForCatalogAsync(vm);
        Assert.True(vm.IsEdgeEditor);
        Assert.Null(vm.DraftVoice);
        Assert.False(vm.CanTestDraft);
        Assert.False(vm.HasUnsavedChanges);
        vm.ChangeVoiceCommand.Execute(null);
        Assert.True(vm.IsVoicePickerOpen);
        Assert.True(vm.TryHandleEscape());
        Assert.False(vm.IsVoicePickerOpen);
        var voice = Assert.Single(vm.Voices);
        vm.SelectVoiceCommand.Execute(voice);
        Assert.True(vm.HasUnsavedChanges);
        await vm.TestDraftCommand.ExecuteAsync(null);
        Assert.Equal(voice, Assert.IsType<EdgeSpeechProviderConfiguration>(fixture.EdgeRuntime.Provider!.Configuration).Voice);
        Assert.Null(Assert.IsType<EdgeSpeechProviderConfiguration>(fixture.Store.Items.Single(item => item.Id == edge.Id).Configuration).Voice);
        Assert.Equal(fixture.First.Id, fixture.Settings.Current.CurrentProviderId);
        await vm.SaveDraftCommand.ExecuteAsync(null);
        Assert.Equal(voice, Assert.IsType<EdgeSpeechProviderConfiguration>(fixture.Store.Items.Single(item => item.Id == edge.Id).Configuration).Voice);
        Assert.False(vm.HasUnsavedChanges);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Edge_refresh_failure_and_missing_voice_keep_saved_configuration_and_readiness()
    {
        var fixture = new Fixture();
        var voice = new EdgeVoice("saved", "Offline voice", "zh-CN", "Female");
        var edge = await fixture.EnableEdgeAsync(voice);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers.Single(item => item.Id == edge.Id));
        await WaitForCatalogAsync(vm);
        Assert.Equal("当前 Voice 未出现在最新列表中", vm.VoiceCatalogMessage);
        Assert.Equal(voice, vm.DraftVoice);
        Assert.True(vm.CanTestDraft);
        Assert.True(ProviderConfigurationValidator.Validate(edge).IsValid);
        var oldCatalog = vm.Voices.ToArray();
        fixture.Transport.Failure = new IOException("fixture failure");
        await vm.RefreshVoiceCatalogCommand.ExecuteAsync(null);
        Assert.Contains("配置已保留", vm.VoiceCatalogMessage);
        Assert.Equal(oldCatalog, vm.Voices);
        Assert.False(vm.HasUnsavedChanges);
        Assert.True(vm.CanTestDraft);
        Assert.Equal(voice, vm.DraftVoice);
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
    }

    [Fact]
    public async Task Pending_catalog_keeps_editor_usable_and_cannot_update_a_later_http_editor()
    {
        var fixture = new Fixture();
        fixture.Transport.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var voice = new EdgeVoice("saved", "Offline voice", "zh-CN", "Female");
        var edge = await fixture.EnableEdgeAsync(voice);
        var vm = fixture.ViewModel;
        await vm.LoadAsync(CancellationToken.None);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers.Single(item => item.Id == edge.Id));
        await fixture.Transport.Entered.Task;
        Assert.True(vm.IsVoiceCatalogLoading);
        Assert.True(vm.CanTestDraft);
        Assert.True(vm.CanCancelEditing);
        await vm.TestDraftCommand.ExecuteAsync(null);
        Assert.True(fixture.Player.IsPlaying);
        await vm.SelectProviderCommand.ExecuteAsync(vm.Providers.Single(item => item.Id == fixture.First.Id));
        fixture.Transport.Release.SetResult();
        vm.HandleNavigatedFrom();
        await vm.FinishDeactivationAsync();
        Assert.True(vm.IsHttpEditor);
        Assert.Empty(vm.Voices);
        Assert.Equal(string.Empty, vm.VoiceCatalogMessage);
        Assert.Equal(voice, Assert.IsType<EdgeSpeechProviderConfiguration>(fixture.Store.Items.Single(item => item.Id == edge.Id).Configuration).Voice);
    }

    [Fact]
    public async Task Experimental_toggle_runs_edge_lifecycle_and_reverts_failed_enable()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.LoadAsync(CancellationToken.None);
        var features = new ExperimentalFeaturesService(fixture.Settings);
        var vm = new ExperimentalFeaturesViewModel(features,
            new SpeechProviderWorkspace(fixture.Store, TimeProvider.System, fixture.Settings),
            new FakeNavigationService(), new FakeFeedbackService());
        await vm.LoadAsync(CancellationToken.None);
        var item = Assert.Single(vm.Features);
        fixture.Store.SaveFailure = new IOException("fixture failure");
        item.IsEnabled = true;
        Assert.False(item.IsEnabled);
        Assert.True(item.CanToggle);
        Assert.False(features.IsEnabled(item.Id));
        fixture.Store.SaveFailure = null;
        item.IsEnabled = true;
        var edge = Assert.Single(fixture.Store.Items, provider => provider.Type == SpeechProviderType.MicrosoftEdge);
        var edgeItem = Assert.Single(fixture.ViewModel.Providers, provider => provider.Id == edge.Id);
        Assert.False(edgeItem.CanShare);
        Assert.False(edgeItem.CanDelete);
        Assert.True(features.IsEnabled(item.Id));
        fixture.Settings.SetCurrent(edge.Id);
        item.IsEnabled = false;
        Assert.Equal(fixture.First.Id, Assert.Single(fixture.ViewModel.Providers).Id);
        Assert.False(features.IsEnabled(item.Id));
        Assert.Null(fixture.Settings.Current.CurrentProviderId);
        item.IsEnabled = true;
        Assert.Equal(edge.Id, Assert.Single(fixture.Store.Items, provider => provider.Type == SpeechProviderType.MicrosoftEdge).Id);
        Assert.Null(fixture.Settings.Current.CurrentProviderId);
        vm.Deactivate();
        fixture.ViewModel.HandleNavigatedFrom();
        await fixture.ViewModel.FinishDeactivationAsync();
    }

    private static async Task WaitForCatalogAsync(SpeechServicesViewModel vm)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(vm.IsVoiceCatalogLoading) && !vm.IsVoiceCatalogLoading) ready.TrySetResult();
        }
        vm.PropertyChanged += Changed;
        try
        {
            if (!vm.IsVoiceCatalogLoading) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { vm.PropertyChanged -= Changed; }
    }

    private static SpeechProviderInstance CreateProvider(string name, int order) => new(ProviderId.New(), name, order,
        new HttpSpeechProviderConfiguration("https://example.invalid/audio", "GET", new Dictionary<string, string>(), null, null),
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class Fixture
    {
        public SpeechProviderInstance First { get; } = CreateProvider("First", 0);
        public Store Store { get; } = new();
        public FakeAppSettingsService Settings { get; } = new();
        public FakeAppDialogService Dialogs { get; } = new();
        public FakeRuleDocumentInteraction Documents { get; } = new();
        public Runtime Runtime { get; } = new();
        public Runtime EdgeRuntime { get; } = new(SpeechProviderType.MicrosoftEdge);
        public Transport Transport { get; } = new();
        public Player Player { get; } = new();
        public FakeFeedbackService Feedback { get; } = new();
        public SpeechServicesViewModel ViewModel { get; }
        public async Task<SpeechProviderInstance> EnableEdgeAsync(EdgeVoice? voice)
        {
            var workspace = new SpeechProviderWorkspace(Store, TimeProvider.System, Settings);
            await workspace.SetEdgeEnabledAsync(true, CancellationToken.None);
            var edge = ItemsEdge();
            if (voice is not null)
                edge = await workspace.SaveAsync(edge with { Configuration = new EdgeSpeechProviderConfiguration(voice) }, false, CancellationToken.None);
            return edge;
        }
        private SpeechProviderInstance ItemsEdge() => Store.Items.Single(item => item.Type == SpeechProviderType.MicrosoftEdge);
        public Fixture()
        {
            Store.Items.Add(First);
            Settings.SetCurrent(First.Id);
            ViewModel = new SpeechServicesViewModel(Store, new SpeechProviderWorkspace(Store, TimeProvider.System, Settings),
                new ProviderDraftPreviewService([Runtime, EdgeRuntime], Settings, Player), Settings, Feedback, Dialogs,
                new FakeNavigationService(), Documents, new InlineScheduler(), new EdgeVoiceCatalog(Transport, TimeProvider.System));
        }
    }

    private sealed class Store : IProviderStore
    {
        public List<SpeechProviderInstance> Items { get; } = [];
        public Exception? SaveFailure { get; set; }
        public ProviderId? FailedDeleteId { get; set; }
        public Task<IReadOnlyList<SpeechProviderInstance>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpeechProviderInstance>>(Items.OrderBy(provider => provider.SortOrder).ToArray());
        public Task<SpeechProviderInstance?> GetByIdAsync(ProviderId id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(provider => provider.Id == id));
        public Task SaveAsync(SpeechProviderInstance provider, CancellationToken cancellationToken)
        {
            if (SaveFailure is not null) throw SaveFailure;
            Items.RemoveAll(item => item.Id == provider.Id);
            Items.Add(provider);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(ProviderId id, CancellationToken cancellationToken)
        {
            if (id == FailedDeleteId) throw new InvalidOperationException("fixture delete failure");
            Items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }
        public async Task InsertAfterAsync(SpeechProviderInstance provider, ProviderId precedingId, CancellationToken cancellationToken)
        {
            var ids = Items.OrderBy(item => item.SortOrder).Select(item => item.Id).ToList();
            ids.Insert(ids.IndexOf(precedingId) + 1, provider.Id);
            await SaveAsync(provider, cancellationToken);
            await UpdateSortOrderAsync(ids, cancellationToken);
        }
        public Task UpdateSortOrderAsync(IReadOnlyList<ProviderId> order, CancellationToken cancellationToken)
        {
            for (var index = 0; index < order.Count; index++)
            {
                var itemIndex = Items.FindIndex(provider => provider.Id == order[index]);
                Items[itemIndex] = Items[itemIndex] with { SortOrder = index };
            }
            return Task.CompletedTask;
        }
    }

    private sealed class Transport : IEdgeSpeechTransport
    {
        public Exception? Failure { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release { get; set; }
        public async Task<IReadOnlyList<EdgeVoice>> GetVoicesAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            return [new EdgeVoice("available", "Available voice", "en-US", "Female")];
        }
        public Task<ProviderSynthesisResult> SynthesizeAsync(EdgeVoice voice, string text, int ratePercent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Runtime(SpeechProviderType type = SpeechProviderType.Http) : IProviderRuntime
    {
        public SpeechProviderType Type => type;
        public SpeechProviderInstance? Provider { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release { get; set; }
        public async Task<ProviderSynthesisResult> SynthesizeAsync(SpeechProviderInstance provider,
            ProviderSynthesisRequest request, CancellationToken cancellationToken)
        {
            Provider = provider;
            Entered.TrySetResult();
            if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
            return new ProviderSynthesisResult(new MemoryStream([1]), "wav", null);
        }
    }

    private sealed class Player : IProviderPreviewAudioPlayer
    {
        public bool IsPlaying { get; private set; }
        public event EventHandler<ProviderPreviewPlaybackFailedEventArgs>? PlaybackFailed { add { } remove { } }
        public Task<ProviderPreviewPlaybackResult> PlayAsync(Stream audio, string? audioFormat, CancellationToken cancellationToken)
        {
            IsPlaying = true;
            return Task.FromResult(new ProviderPreviewPlaybackResult(true, null));
        }
        public Task StopAsync(CancellationToken cancellationToken) { IsPlaying = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InlineScheduler : IUiScheduler
    {
        public bool CheckAccess() => true;
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; }
        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return action(); }
    }

    private sealed class FakeFeedbackService : IAppFeedbackService
    {
        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }
        public bool LastWasWarning { get; private set; }
        public int DeletionPromptCount { get; private set; }

        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastTitle = title;
            LastMessage = projected.UserMessage;
        }

        public void ShowSuccess(string title, string message)
        {
            LastWasWarning = false;
            LastTitle = title;
            LastMessage = message;
        }

        public void ShowWarning(string title, string message)
        {
            LastWasWarning = true;
            LastTitle = title;
            LastMessage = message;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            DeletionPromptCount++;
            return Task.FromResult(AppConfirmationDecision.Confirm);
        }
    }

    private sealed class FakeAppDialogService : IAppDialogService
    {
        public UnsavedChangesDecision NextUnsavedDecision { get; set; } = UnsavedChangesDecision.Discard;

        public AppConfirmationDecision NextConfirmationDecision { get; set; } = AppConfirmationDecision.Cancel;

        public int UnsavedChangesPromptCount { get; private set; }

        public Task<AppConfirmationDecision> ShowConfirmationAsync(string title, string message, string primaryButtonText, string closeButtonText, CancellationToken cancellationToken)
        {
            return Task.FromResult(NextConfirmationDecision);
        }

        public Task<UnsavedChangesDecision> ShowUnsavedChangesAsync(string title, string message, string saveButtonText, string discardButtonText, string cancelButtonText, CancellationToken cancellationToken)
        {
            UnsavedChangesPromptCount++;
            return Task.FromResult(NextUnsavedDecision);
        }
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default with { DefaultSpeakSpeed = 12 };
        public void SetCurrent(ProviderId id) => Current = Current with { CurrentProviderId = id };
        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = Current;
            Current = Current with { CurrentProviderId = update.ClearCurrentProvider ? null : update.CurrentProviderId ?? Current.CurrentProviderId };
            if (update.ExperimentalFeatureChange is { } change)
            {
                var ids = Current.EnabledExperimentalFeatureIds?.ToHashSet(StringComparer.Ordinal) ?? [];
                if (change.Enabled) ids.Add(change.FeatureId); else ids.Remove(change.FeatureId);
                Current = Current with { EnabledExperimentalFeatureIds = ids.ToArray() };
            }
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, Current));
            return Task.FromResult(Current);
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
}
