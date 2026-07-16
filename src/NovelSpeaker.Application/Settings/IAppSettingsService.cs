using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Provides serialized reads and partial updates for user settings.
/// </summary>
public interface IAppSettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettingsChangedEventArgs>? Changed;

    Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken);
}
