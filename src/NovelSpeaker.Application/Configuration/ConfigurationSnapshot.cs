using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Configuration;

/// <summary>A private configuration snapshot; never includes library or derived data.</summary>
public sealed record ConfigurationSnapshot(
    AppSettings Settings,
    IReadOnlyList<SpeechProviderInstance> Providers,
    IReadOnlyList<ChapterRule> ChapterRules,
    IReadOnlyList<RegexReplacementRule> RegexReplacementRules,
    IReadOnlyList<FileNameMetadataRule> FileNameMetadataRules,
    IReadOnlyList<TextHeaderMetadataRule> TextHeaderMetadataRules);

/// <summary>Coordinates the existing configuration stores as one replacement boundary.</summary>
public interface IConfigurationSnapshotStore
{
    Task<ConfigurationSnapshot> ReadAsync(AppSettings settings, CancellationToken cancellationToken);

    // Must leave the old configuration intact on failure. Runtime publication happens after success.
    Task ReplaceAsync(ConfigurationSnapshot snapshot, AppSettings previousSettings, CancellationToken cancellationToken);
}
