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

        var namesInUse = new HashSet<string>(existing.Select(provider => provider.Name),
            StringComparer.OrdinalIgnoreCase);
        var portableInputs = existing
            .Where(provider => provider.Configuration is HttpSpeechProviderConfiguration)
            .Select(provider => (provider.Name, Configuration: (HttpSpeechProviderConfiguration)provider.Configuration))
            .ToList();
        var nextSortOrder = existing.Count == 0 ? 0L : (long)existing.Max(provider => provider.SortOrder) + 1;
        var items = new List<ProviderImportPlanItem>(document.Items.Count);
        foreach (var source in document.Items)
        {
            if (!source.IsValid)
            {
                items.Add(new ProviderImportPlanItem(source.Index, ProviderImportPlanStatus.Invalid,
                    null, source.Error));
                continue;
            }

            var name = source.Name!;
            var config = source.Configuration!;
            if (portableInputs.Any(entry => HttpProviderConfigurationComparer.IsSamePortableProvider(
                    entry.Name, entry.Configuration, name, config)))
            {
                items.Add(new ProviderImportPlanItem(source.Index, ProviderImportPlanStatus.Duplicate,
                    null, "名称与配置均相同，已跳过。"));
                continue;
            }

            if (nextSortOrder > int.MaxValue)
            {
                items.Add(new ProviderImportPlanItem(source.Index, ProviderImportPlanStatus.Invalid,
                    null, "Provider 排序空间不足。"));
                continue;
            }

            var uniqueName = MakeUniqueName(name, namesInUse);
            var candidate = new SpeechProviderInstance(
                ProviderId.New(), uniqueName, (int)nextSortOrder++,
                config, now, now);
            items.Add(new ProviderImportPlanItem(source.Index, ProviderImportPlanStatus.Ready, candidate, null));
            namesInUse.Add(uniqueName);
            portableInputs.Add((name, config));
            if (!string.Equals(uniqueName, name, StringComparison.OrdinalIgnoreCase))
            {
                portableInputs.Add((uniqueName, config));
            }
        }

        return new ProviderImportPlan(items, null);
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
