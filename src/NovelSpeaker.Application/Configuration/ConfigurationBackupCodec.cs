using System.Text.Json;
using System.Text.Json.Serialization;
using NovelSpeaker.Application.Books.RuleEditing;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Configuration;

/// <summary>Version 1 private backup format, deliberately separate from share envelopes.</summary>
public static class ConfigurationBackupCodec
{
    private const string Format = "NovelSpeaker.ConfigurationBackup";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public static string Write(ConfigurationSnapshot snapshot)
    {
        Validate(snapshot);
        return JsonSerializer.Serialize(ToDocument(snapshot), Options);
    }

    public static ConfigurationSnapshot Read(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            RejectDuplicateProperties(parsed.RootElement);
            var document = parsed.RootElement.Deserialize<BackupDocument>(Options);
            if (document is null || document.Format != Format || document.SchemaVersion != 1)
                throw Invalid();
            var snapshot = new ConfigurationSnapshot(document.Settings,
                document.Providers.Select(provider => provider.ToInstance()).ToArray(),
                document.ChapterRules, document.RegexReplacementRules,
                document.FileNameMetadataRules, document.TextHeaderMetadataRules);
            Validate(snapshot);
            // All settings and nested fields are required, even where domain constructors have defaults.
            using var expected = JsonDocument.Parse(JsonSerializer.Serialize(ToDocument(snapshot), Options));
            RequireFields(parsed.RootElement, expected.RootElement);
            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or
            InvalidOperationException or NotSupportedException or NullReferenceException)
        {
            // Do not expose parser messages, paths, patterns or credentials to feedback/logs.
            throw Invalid();
        }
    }

    private static InvalidOperationException Invalid() =>
        new("配置备份损坏、内容无效或版本不受支持，未修改当前配置。");

    internal static void Validate(ConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var settings = snapshot.Settings;
        if (settings is null || settings != settings.Normalize() with
        { EnabledExperimentalFeatureIds = settings.EnabledExperimentalFeatureIds } ||
            settings.EnabledExperimentalFeatureIds is null ||
            settings.EnabledExperimentalFeatureIds.Any(string.IsNullOrWhiteSpace) ||
            settings.EnabledExperimentalFeatureIds.Distinct(StringComparer.Ordinal).Count() != settings.EnabledExperimentalFeatureIds.Count)
            throw Invalid();

        var ids = new HashSet<ProviderId>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edgeCount = 0;
        foreach (var provider in snapshot.Providers)
        {
            if (provider is null || provider.Id.Value == Guid.Empty || !ids.Add(provider.Id) ||
                !names.Add(provider.Name) || provider.SortOrder < 0 ||
                !SpeechProviderNameRules.TryNormalize(provider.Name, out var name) || name != provider.Name)
                throw Invalid();
            if (provider.Configuration is EdgeSpeechProviderConfiguration { Voice: null })
            {
                if (provider.Name != SpeechProviderNameRules.MicrosoftEdgeName) throw Invalid();
            }
            else if (!ProviderConfigurationValidator.Validate(provider).IsValid) throw Invalid();
            if (provider.Type == SpeechProviderType.MicrosoftEdge && ++edgeCount > 1) throw Invalid();
        }
        if (settings.CurrentProviderId is { } current &&
            (!ids.Contains(current) || snapshot.Providers.Any(provider => provider.Id == current &&
                (!ProviderConfigurationValidator.Validate(provider).IsValid ||
                 provider.Type == SpeechProviderType.MicrosoftEdge &&
                 !settings.EnabledExperimentalFeatureIds.Contains(ExperimentalFeaturesService.MicrosoftEdgeTts)))))
            throw Invalid();

        ValidateRules(snapshot.ChapterRules.Select(rule => (rule.Id, rule.Name, rule.Pattern, rule.SortOrder)));
        ValidateRules(snapshot.FileNameMetadataRules.Select(rule => (rule.Id, rule.Name, rule.Pattern, rule.SortOrder)));
        ValidateRules(snapshot.TextHeaderMetadataRules.Select(rule => (rule.Id, rule.Name, rule.Pattern, rule.SortOrder)));
        ValidateRules(snapshot.RegexReplacementRules.Select(rule =>
            (rule.Id == Guid.Empty ? "" : rule.Id.ToString(), rule.Name, rule.Pattern, rule.SortOrder)));
        if (snapshot.RegexReplacementRules.Any(rule => rule.Replacement is null || !Enum.IsDefined(rule.Scope))) throw Invalid();
    }

    private static void ValidateRules(IEnumerable<(string Id, string Name, string Pattern, int SortOrder)> rules)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) ||
                string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.Pattern) ||
                rule.SortOrder < 0 || !RulePatternValidation.IsValid(rule.Pattern, TimeSpan.FromMilliseconds(100)))
                throw Invalid();
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static void RequireFields(JsonElement actual, JsonElement expected)
    {
        if (expected.ValueKind == JsonValueKind.Object)
            foreach (var property in expected.EnumerateObject())
            {
                if (!actual.TryGetProperty(property.Name, out var value)) throw Invalid();
                RequireFields(value, property.Value);
            }
        else if (expected.ValueKind == JsonValueKind.Array)
            foreach (var pair in actual.EnumerateArray().Zip(expected.EnumerateArray()))
                RequireFields(pair.First, pair.Second);
    }

    private static BackupDocument ToDocument(ConfigurationSnapshot snapshot) => new(Format, 1,
        snapshot.Settings, snapshot.Providers.Select(BackupProvider.FromInstance).ToArray(),
        snapshot.ChapterRules, snapshot.RegexReplacementRules,
        snapshot.FileNameMetadataRules, snapshot.TextHeaderMetadataRules);

    private sealed record BackupDocument(string Format, int SchemaVersion, AppSettings Settings,
        IReadOnlyList<BackupProvider> Providers, IReadOnlyList<ChapterRule> ChapterRules,
        IReadOnlyList<RegexReplacementRule> RegexReplacementRules,
        IReadOnlyList<FileNameMetadataRule> FileNameMetadataRules,
        IReadOnlyList<TextHeaderMetadataRule> TextHeaderMetadataRules);

    private sealed record BackupProvider(ProviderId Id, string Name, int SortOrder,
        SpeechProviderType Type, HttpSpeechProviderConfiguration? Http,
        EdgeSpeechProviderConfiguration? Edge, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
    {
        public static BackupProvider FromInstance(SpeechProviderInstance provider) => new(provider.Id,
            provider.Name, provider.SortOrder, provider.Type,
            provider.Configuration as HttpSpeechProviderConfiguration,
            provider.Configuration as EdgeSpeechProviderConfiguration, provider.CreatedAt, provider.UpdatedAt);

        public SpeechProviderInstance ToInstance() => new(Id, Name, SortOrder, Type switch
        {
            SpeechProviderType.Http when Http is not null && Edge is null && Http.Type == Type => Http,
            SpeechProviderType.MicrosoftEdge when Edge is not null && Http is null && Edge.Type == Type => Edge,
            _ => throw Invalid()
        }, CreatedAt, UpdatedAt);
    }
}
