using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Provides serialized reads and partial updates for user settings.
/// </summary>
public interface IAppSettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken);
}
