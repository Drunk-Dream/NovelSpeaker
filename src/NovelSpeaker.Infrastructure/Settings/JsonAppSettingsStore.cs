using System.Globalization;
using System.Text.Json;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

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
    private readonly TimeProvider _timeProvider;
    private readonly ISettingsFileOperations _files;

    public JsonAppSettingsStore(IAppDataDirectoryProvider directories, TimeProvider? timeProvider = null)
        : this(directories, timeProvider ?? TimeProvider.System, PhysicalSettingsFileOperations.Instance)
    {
    }

    internal JsonAppSettingsStore(
        IAppDataDirectoryProvider directories,
        TimeProvider timeProvider,
        ISettingsFileOperations files)
    {
        _directories = directories;
        _timeProvider = timeProvider;
        _files = files;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_files.Exists(_directories.SettingsPath))
        {
            return AppSettings.Default;
        }

        try
        {
            await using var stream = _files.OpenRead(_directories.SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return (settings ?? AppSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            IsolateCorruptFile();
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _directories.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var temporaryPath = $"{_directories.SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var normalized = settings.Normalize();
            await using (var stream = _files.CreateForWrite(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                _files.FlushToDisk(stream);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _files.Move(temporaryPath, _directories.SettingsPath, overwrite: true);
        }
        finally
        {
            if (_files.Exists(temporaryPath))
            {
                _files.Delete(temporaryPath);
            }
        }
    }

    private void IsolateCorruptFile()
    {
        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        for (var suffix = 0; ; suffix++)
        {
            var suffixText = suffix == 0 ? string.Empty : $".{suffix}";
            var backupPath = $"{_directories.SettingsPath}.{timestamp}{suffixText}.corrupt";
            if (_files.Exists(backupPath))
            {
                continue;
            }

            try
            {
                _files.Move(_directories.SettingsPath, backupPath, overwrite: false);
                return;
            }
            catch (IOException) when (_files.Exists(backupPath))
            {
            }
        }
    }
}
