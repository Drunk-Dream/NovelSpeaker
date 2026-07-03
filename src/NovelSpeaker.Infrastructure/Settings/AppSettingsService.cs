using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Infrastructure.Settings;

/// <summary>
/// Serializes partial settings updates so concurrent callers do not overwrite newer values.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IAppSettingsStore _store;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private AppSettings? _cachedSettings;

    public AppSettingsService(IAppSettingsStore store)
    {
        _store = store;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
            var next = ApplyUpdate(current, update).Normalize();
            await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            _cachedSettings = next;
            return next;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<AppSettings> LoadCurrentAsync(CancellationToken cancellationToken)
    {
        if (_cachedSettings is not null)
        {
            return _cachedSettings;
        }

        _cachedSettings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return _cachedSettings;
    }

    private static AppSettings ApplyUpdate(AppSettings current, AppSettingsUpdate update)
    {
        return current with
        {
            EnableLongParagraphSplitting = update.EnableLongParagraphSplitting ?? current.EnableLongParagraphSplitting,
            LongParagraphThreshold = update.LongParagraphThreshold ?? current.LongParagraphThreshold,
            DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? current.DefaultSpeakSpeed,
            PrefetchCount = update.PrefetchCount ?? current.PrefetchCount,
            LogLevel = update.LogLevel ?? current.LogLevel,
            Theme = update.Theme ?? current.Theme,
            BookFileNameTemplate = update.BookFileNameTemplate ?? current.BookFileNameTemplate,
            SelectedTtsRuleId = update.ClearSelectedTtsRuleId ? null : update.SelectedTtsRuleId ?? current.SelectedTtsRuleId
        };
    }
}
