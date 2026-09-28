using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.PresentationTests.TestDoubles;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.ViewModels;

public sealed class SpeechServicesViewModelTests
{
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
        public Player Player { get; } = new();
        public SpeechServicesViewModel ViewModel { get; }
        public Fixture()
        {
            Store.Items.Add(First);
            Settings.SetCurrent(First.Id);
            ViewModel = new SpeechServicesViewModel(Store, new SpeechProviderWorkspace(Store, TimeProvider.System, Settings),
                new HttpProviderDraftPreviewService([Runtime], Settings, Player), Settings, new FakeFeedbackService(), Dialogs,
                new FakeNavigationService(), Documents, new InlineScheduler());
        }
    }

    private sealed class Store : IProviderStore
    {
        public List<SpeechProviderInstance> Items { get; } = [];
        public Exception? SaveFailure { get; set; }
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

    private sealed class Runtime : IProviderRuntime
    {
        public SpeechProviderType Type => SpeechProviderType.Http;
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

        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastTitle = title;
            LastMessage = projected.UserMessage;
        }

        public void ShowSuccess(string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }

        public void ShowWarning(string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
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
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            Current = Current with { CurrentProviderId = update.ClearCurrentProvider ? null : update.CurrentProviderId ?? Current.CurrentProviderId };
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
