using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Speech.Providers;

public enum ProviderImportExecutionStatus
{
    Imported,
    Duplicate,
    Failed
}

public sealed record ProviderImportExecutionItem(
    int Index,
    ProviderImportExecutionStatus Status,
    ProviderId? ImportedId,
    string? Message);

public sealed record ProviderImportExecutionResult(
    IReadOnlyList<ProviderImportExecutionItem> Items,
    string? Error)
{
    public int ImportedCount => Items.Count(item => item.Status == ProviderImportExecutionStatus.Imported);
    public int DuplicateCount => Items.Count(item => item.Status == ProviderImportExecutionStatus.Duplicate);
    public int FailedCount => Items.Count(item => item.Status == ProviderImportExecutionStatus.Failed);
}

public enum ProviderExportStatus
{
    ConfirmationRequired,
    Ready,
    ProviderUnavailable
}

public sealed record ProviderExportResult(
    ProviderExportStatus Status,
    string? Json,
    string Message);

/// <summary>Owns Provider management mutations, HTTP drafts, imports, and export confirmation.</summary>
public sealed class SpeechProviderWorkspace(IProviderStore store, TimeProvider timeProvider, IAppSettingsService settings)
{
    public bool IsVisible(SpeechProviderInstance provider) => provider.Type != SpeechProviderType.MicrosoftEdge ||
        settings.Current.EnabledExperimentalFeatureIds?.Contains(ExperimentalFeaturesService.MicrosoftEdgeTts) == true;

    public async Task SetEdgeEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var edge = existing.SingleOrDefault(provider => provider.Type == SpeechProviderType.MicrosoftEdge);
            if (enabled && edge is null)
            {
                var now = timeProvider.GetUtcNow();
                edge = new SpeechProviderInstance(ProviderId.New(), SpeechProviderNameRules.MicrosoftEdgeName,
                    NextSortOrder(existing), new EdgeSpeechProviderConfiguration(null), now, now);
                await store.SaveAsync(edge, cancellationToken).ConfigureAwait(false);
            }
            await settings.UpdateAsync(new AppSettingsUpdate
            {
                ClearCurrentProvider = !enabled && edge is not null && edge.Id == settings.Current.CurrentProviderId,
                ExperimentalFeatureChange = new ExperimentalFeatureChange(ExperimentalFeaturesService.MicrosoftEdgeTts, enabled)
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _mutationGate.Release(); }
    }

    public const string ExportWarning =
        "导出文件可能包含 API Key、Token、Cookie 等凭据。请确认保存位置或剪贴板接收方可信。";

    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public async Task<SpeechProviderInstance> CreateDraftAsync(CancellationToken cancellationToken)
    {
        var existing = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var names = new HashSet<string>(existing.Select(provider => provider.Name), StringComparer.OrdinalIgnoreCase);
        var name = ProviderImportPlanner.MakeUniqueName("HTTP Provider", names);
        var now = timeProvider.GetUtcNow();
        return new SpeechProviderInstance(ProviderId.New(), name, 0,
            new HttpSpeechProviderConfiguration(string.Empty, "GET",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, null),
            now, now);
    }

    public async Task<SpeechProviderInstance> SaveAsync(
        SpeechProviderInstance draft,
        bool createNew,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var original = createNew ? null : existing.FirstOrDefault(provider => provider.Id == draft.Id);
            if (!createNew && original is null)
            {
                throw new InvalidOperationException("要编辑的 Provider 不存在。");
            }

            if (draft.Type == SpeechProviderType.MicrosoftEdge &&
                (createNew || original?.Type != SpeechProviderType.MicrosoftEdge || !IsVisible(original)))
                throw new InvalidOperationException("Microsoft Edge 只能编辑已启用的内置实例。");
            if (original is not null && original.Type != draft.Type)
                throw new InvalidOperationException("不能更改语音服务类型。");

            var now = timeProvider.GetUtcNow();
            var normalizedName = draft.Name?.Trim() ?? string.Empty;
            var sortOrder = createNew ? NextSortOrder(existing) : original!.SortOrder;
            var provider = draft with
            {
                Id = createNew ? ProviderId.New() : original!.Id,
                Name = normalizedName,
                SortOrder = sortOrder,
                CreatedAt = createNew ? now : original!.CreatedAt,
                UpdatedAt = now
            };
            var validation = ProviderConfigurationValidator.Validate(provider);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors));
            }

            if (existing.Any(item => item.Id != provider.Id &&
                                     item.Name.Equals(provider.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Provider 名称必须全局唯一（忽略大小写）。");
            }

            await store.SaveAsync(provider, cancellationToken).ConfigureAwait(false);
            return provider;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ProviderImportExecutionResult> ImportAsync(string json, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var document = ProviderEnvelopeCodec.Read(json);
            if (!document.IsValid)
            {
                return new ProviderImportExecutionResult([], document.Error);
            }

            var accepted = existing.ToList();
            var successfulInputs = new List<PortableProviderIdentity>();
            var results = new List<ProviderImportExecutionItem>(document.Items.Count);
            foreach (var source in document.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = ProviderImportPlanner.PlanOne(source, accepted, successfulInputs,
                    timeProvider.GetUtcNow());
                if (item.Status == ProviderImportPlanStatus.Duplicate)
                {
                    results.Add(new ProviderImportExecutionItem(item.Index,
                        ProviderImportExecutionStatus.Duplicate, null, item.Message));
                    continue;
                }

                if (item.Candidate is null)
                {
                    results.Add(new ProviderImportExecutionItem(item.Index,
                        ProviderImportExecutionStatus.Failed, null, item.Message));
                    continue;
                }

                try
                {
                    await store.SaveAsync(item.Candidate, cancellationToken).ConfigureAwait(false);
                    accepted.Add(item.Candidate);
                    successfulInputs.Add(new PortableProviderIdentity(source.Name!, source.Configuration!));
                    results.Add(new ProviderImportExecutionItem(item.Index,
                        ProviderImportExecutionStatus.Imported, item.Candidate.Id, null));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    results.Add(new ProviderImportExecutionItem(item.Index,
                        ProviderImportExecutionStatus.Failed, null, "Provider 保存失败。"));
                }
            }

            return new ProviderImportExecutionResult(results, null);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ProviderExportResult> ExportAsync(
        ProviderId providerId,
        bool credentialsWarningAcknowledged,
        CancellationToken cancellationToken)
    {
        var provider = await store.GetByIdAsync(providerId, cancellationToken).ConfigureAwait(false);
        if (provider is null || provider.Configuration is not HttpSpeechProviderConfiguration ||
            !ProviderConfigurationValidator.Validate(provider).IsValid)
        {
            return new ProviderExportResult(ProviderExportStatus.ProviderUnavailable, null,
                "HTTP Provider 不可导出。");
        }

        return credentialsWarningAcknowledged
            ? new ProviderExportResult(ProviderExportStatus.Ready,
                ProviderEnvelopeCodec.Write(provider), string.Empty)
            : new ProviderExportResult(ProviderExportStatus.ConfirmationRequired, null, ExportWarning);
    }

    public async Task<SpeechProviderInstance> CopyAsync(ProviderId sourceId, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var source = existing.FirstOrDefault(provider => provider.Id == sourceId);
            if (source?.Configuration is not HttpSpeechProviderConfiguration http)
            {
                throw new InvalidOperationException("该语音服务不可复制。");
            }
            var names = existing.Select(provider => provider.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now = timeProvider.GetUtcNow();
            var copy = source with
            {
                Id = ProviderId.New(),
                Name = ProviderImportPlanner.MakeUniqueName($"{source.Name} (副本)", names),
                Configuration = http with { Headers = new Dictionary<string, string>(http.Headers, StringComparer.OrdinalIgnoreCase) },
                CreatedAt = now,
                UpdatedAt = now
            };
            await store.InsertAfterAsync(copy, source.Id, cancellationToken).ConfigureAwait(false);
            return await store.GetByIdAsync(copy.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("复制的语音服务不可用。");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task ReorderAsync(IReadOnlyList<ProviderId> orderedIds, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
            if (orderedIds.Count != existing.Count || orderedIds.Distinct().Count() != orderedIds.Count ||
                !existing.Select(provider => provider.Id).ToHashSet().SetEquals(orderedIds))
            {
                throw new InvalidOperationException("语音服务列表已变化，请刷新后重试。");
            }

            await store.UpdateSortOrderAsync(orderedIds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task DeleteAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var provider = await store.GetByIdAsync(providerId, cancellationToken).ConfigureAwait(false);
            if (provider?.Type != SpeechProviderType.Http)
            {
                throw new InvalidOperationException("该语音服务不可删除。");
            }

            // Clear selection before deleting: settings failures must not leave a deleted current Provider.
            var wasCurrent = settings.Current.CurrentProviderId == providerId;
            if (wasCurrent)
            {
                await settings.UpdateAsync(new AppSettingsUpdate { ClearCurrentProvider = true }, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                await store.DeleteAsync(providerId, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (wasCurrent && settings.Current.CurrentProviderId is null)
                {
                    await settings.UpdateAsync(new AppSettingsUpdate { CurrentProviderId = providerId },
                        CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private static int NextSortOrder(IReadOnlyList<SpeechProviderInstance> existing)
    {
        if (existing.Count == 0)
        {
            return 0;
        }

        var maximum = existing.Max(provider => provider.SortOrder);
        if (maximum == int.MaxValue)
        {
            throw new InvalidOperationException("Provider 排序空间不足。");
        }

        return maximum + 1;
    }
}
