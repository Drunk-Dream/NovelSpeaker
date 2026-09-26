using System.Globalization;
using System.Text.Json;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.Infrastructure.Settings;

/// <summary>
/// Reads and writes the desktop settings JSON file.
/// </summary>
public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ISettingsFileOperations _files;

    public JsonAppSettingsStore(
        IAppDataDirectoryProvider directories,
        TimeProvider? timeProvider = null,
        IAppStoragePathResolver? pathResolver = null)
        : this(
            directories,
            timeProvider ?? TimeProvider.System,
            PhysicalSettingsFileOperations.Instance,
            pathResolver)
    {
    }

    internal JsonAppSettingsStore(
        IAppDataDirectoryProvider directories,
        TimeProvider timeProvider,
        ISettingsFileOperations files,
        IAppStoragePathResolver? pathResolver = null)
    {
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _pathResolver = pathResolver ?? new AppStoragePathResolver(_directories);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settingsPath = _pathResolver.ResolvePath(_directories.SettingsPath);

        if (!await Task.Run(
                () => _files.Exists(settingsPath),
                cancellationToken).ConfigureAwait(false))
        {
            return AppSettings.Default;
        }

        try
        {
            AppSettings normalized;
            bool hasLegacySelection;
            await using (var stream = await Task.Run(
                             () => _files.OpenRead(settingsPath),
                             cancellationToken).ConfigureAwait(false))
            {
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var settings = JsonSerializer.Deserialize<AppSettings>(
                    document.RootElement.GetRawText(),
                    SerializerOptions) ?? AppSettings.Default;
                hasLegacySelection = TryGetProperty(document.RootElement, "SelectedTtsRuleId", out var legacySelection);
                if (settings.CurrentProviderId is null &&
                    legacySelection.ValueKind == JsonValueKind.Number &&
                    legacySelection.TryGetInt64(out var selectedRuleId))
                {
                    settings = settings with
                    {
                        CurrentProviderId = ProviderId.FromLegacyHttpTtsRuleId(selectedRuleId)
                    };
                }

                normalized = settings.Normalize();
            }

            if (hasLegacySelection)
            {
                await SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
            }

            return normalized;
        }
        catch (JsonException)
        {
            await IsolateCorruptFileAsync(cancellationToken).ConfigureAwait(false);
            return AppSettings.Default;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _directories.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var settingsPath = _pathResolver.ResolvePath(_directories.SettingsPath);
        var temporaryPath = _pathResolver.ResolvePath($"{settingsPath}.{Guid.NewGuid():N}.tmp");
        try
        {
            var normalized = settings.Normalize();
            await using (var stream = await Task.Run(
                () => _files.CreateForWrite(temporaryPath),
                cancellationToken).ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                await Task.Run(
                    () => _files.FlushToDisk(stream),
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(
                () => _files.Move(temporaryPath, settingsPath, overwrite: true),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await Task.Run(
                () =>
                {
                    if (_files.Exists(temporaryPath))
                    {
                        _files.Delete(temporaryPath);
                    }
                }).ConfigureAwait(false);
        }
    }

    private Task IsolateCorruptFileAsync(CancellationToken cancellationToken)
    {
        return Task.Run(IsolateCorruptFile, cancellationToken);
    }

    private void IsolateCorruptFile()
    {
        var timestamp = _timeProvider
            .GetUtcNow()
            .ToUniversalTime()
            .ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        var settingsPath = _pathResolver.ResolvePath(_directories.SettingsPath);
        for (var suffix = 0; ; suffix++)
        {
            var suffixText = suffix == 0 ? string.Empty : $".{suffix}";
            var backupPath = _pathResolver.ResolvePath($"{settingsPath}.{timestamp}{suffixText}.corrupt");
            if (_files.Exists(backupPath))
            {
                continue;
            }

            try
            {
                _files.Move(settingsPath, backupPath, overwrite: false);
                return;
            }
            catch (IOException) when (_files.Exists(backupPath))
            {
            }
        }
    }
}
