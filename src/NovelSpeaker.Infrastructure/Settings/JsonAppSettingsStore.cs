using System.Text.Json;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Infrastructure.Settings;

/// <summary>
/// Reads and writes the desktop settings JSON file.
/// </summary>
public sealed class JsonAppSettingsStore :
    IAppSettingsStore,
    ITextSegmentationOptionsProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly IAppDataDirectoryProvider _directories;

    public JsonAppSettingsStore(IAppDataDirectoryProvider directories)
    {
        _directories = directories;
    }

    public TextSegmentationOptions GetCurrent()
    {
        var settings = LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return settings.ToTextSegmentationOptions();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_directories.SettingsPath))
        {
            return AppSettings.Default;
        }

        await using var stream = File.OpenRead(_directories.SettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        return settings is null
            ? AppSettings.Default
            : settings with
            {
                LongParagraphThreshold = settings.ToTextSegmentationOptions().LongParagraphThreshold,
                SelectedTtsRuleId = settings.SelectedTtsRuleId
            };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _directories.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var normalized = settings with
        {
            LongParagraphThreshold = settings.ToTextSegmentationOptions().LongParagraphThreshold,
            SelectedTtsRuleId = settings.SelectedTtsRuleId
        };

        await using var stream = File.Create(_directories.SettingsPath);
        await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
}
