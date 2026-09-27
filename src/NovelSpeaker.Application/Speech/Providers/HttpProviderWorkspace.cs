using NovelSpeaker.Domain.Speech.Providers;

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

/// <summary>Owns HTTP Provider draft saves, independent imports, and export confirmation.</summary>
public sealed class HttpProviderWorkspace(IProviderStore store, TimeProvider timeProvider)
{
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
