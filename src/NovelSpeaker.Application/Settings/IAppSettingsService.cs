using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Owns the process-wide normalized settings snapshot and serialized persisted updates.
/// </summary>
public interface IAppSettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettingsChangedEventArgs>? Changed;

    Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken);
}
