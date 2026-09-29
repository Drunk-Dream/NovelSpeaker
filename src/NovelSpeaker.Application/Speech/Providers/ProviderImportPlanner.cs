using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public enum ProviderImportPlanStatus
{
    Ready,
    Duplicate,
    Invalid
}

public sealed record ProviderImportPlanItem(
    int Index,
    ProviderImportPlanStatus Status,
    SpeechProviderInstance? Candidate,
    string? Message);

public sealed record ProviderImportPlan(
    IReadOnlyList<ProviderImportPlanItem> Items,
    string? Error)
{
    public int ReadyCount => Items.Count(item => item.Status == ProviderImportPlanStatus.Ready);
    public int DuplicateCount => Items.Count(item => item.Status == ProviderImportPlanStatus.Duplicate);
    public int InvalidCount => Items.Count(item => item.Status == ProviderImportPlanStatus.Invalid);
}

internal sealed record PortableProviderIdentity(string Name, HttpSpeechProviderConfiguration Configuration);

/// <summary>Plans independent Provider imports against one current Provider snapshot.</summary>
public static class ProviderImportPlanner
{
    public static ProviderImportPlan Plan(
        string json,
        IReadOnlyList<SpeechProviderInstance> existing,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var document = ProviderEnvelopeCodec.Read(json);
        if (!document.IsValid)
        {
            return new ProviderImportPlan([], document.Error);
        }

        var accepted = existing.ToList();
        var portableInputs = new List<PortableProviderIdentity>();
        var items = new List<ProviderImportPlanItem>(document.Items.Count);
        foreach (var source in document.Items)
        {
            var item = PlanOne(source, accepted, portableInputs, now);
            items.Add(item);
            if (item.Candidate is { } candidate)
            {
                accepted.Add(candidate);
                portableInputs.Add(new PortableProviderIdentity(source.Name!, source.Configuration!));
            }
        }

        return new ProviderImportPlan(items, null);
    }

    internal static ProviderImportPlanItem PlanOne(
        ProviderEnvelopeItem source,
        IReadOnlyList<SpeechProviderInstance> accepted,
        IReadOnlyList<PortableProviderIdentity> successfulInputs,
        DateTimeOffset now)
    {
        if (!source.IsValid)
        {
            return new ProviderImportPlanItem(source.Index, ProviderImportPlanStatus.Invalid, null, source.Error);
        }

        var name = source.Name!;
        var config = source.Configuration!;
        if (accepted.Any(provider => provider.Configuration is HttpSpeechProviderConfiguration http &&
                HttpProviderConfigurationComparer.IsSamePortableProvider(provider.Name, http, name, config)) ||
            successfulInputs.Any(input => HttpProviderConfigurationComparer.IsSamePortableProvider(
                input.Name, input.Configuration, name, config)))
        {
            return new ProviderImportPlanItem(source.Index, ProviderImportPlanStatus.Duplicate,
                null, "名称与配置均相同，已跳过。");
        }

        var nextSortOrder = accepted.Count == 0 ? 0L : (long)accepted.Max(provider => provider.SortOrder) + 1;
        if (nextSortOrder > int.MaxValue)
        {
            return new ProviderImportPlanItem(source.Index, ProviderImportPlanStatus.Invalid,
                null, "Provider 排序空间不足。");
        }

        var namesInUse = new HashSet<string>(accepted.Select(provider => provider.Name),
            StringComparer.OrdinalIgnoreCase);
        var uniqueName = MakeUniqueName(name, namesInUse);
        var candidate = new SpeechProviderInstance(
            ProviderId.New(), uniqueName, (int)nextSortOrder, config, now, now);
        return new ProviderImportPlanItem(source.Index, ProviderImportPlanStatus.Ready, candidate, null);
    }

    public static string MakeUniqueName(string requestedName, IReadOnlySet<string> namesInUse)
    {
        ArgumentNullException.ThrowIfNull(namesInUse);
        var normalized = requestedName.Trim();
        if (!namesInUse.Contains(normalized))
        {
            return normalized;
        }

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = $"{normalized} ({suffix})";
            if (!namesInUse.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("无法生成唯一的 Provider 名称。");
    }
}
