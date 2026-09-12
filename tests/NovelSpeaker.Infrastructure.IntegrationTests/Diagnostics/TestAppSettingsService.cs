using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Diagnostics;

internal sealed class TestAppSettingsService(AppSettings settings) : IAppSettingsService
{
    private readonly AppSettings _settings = settings.Normalize();

    public AppSettings Current => _settings;

    public event EventHandler<AppSettingsChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
        Task.FromResult(_settings);
}
