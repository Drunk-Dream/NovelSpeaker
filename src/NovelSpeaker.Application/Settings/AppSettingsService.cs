using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Owns the process-wide normalized settings snapshot and serializes persisted updates.
/// </summary>
public sealed class AppSettingsService :
    IAppSettingsService,
    IBookFileNameTemplateProvider,
    ITextSegmentationOptionsProvider,
    IDisposable
{
    private readonly IAppSettingsStore _store;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private AppSettings _current;

    public AppSettingsService(
        IAppSettingsStore store,
        AppSettings startupSnapshot)
    {
        _store = store;
        _current = (startupSnapshot ?? throw new ArgumentNullException(nameof(startupSnapshot))).Normalize();
    }

    public AppSettings Current => Volatile.Read(ref _current);

    public event EventHandler<AppSettingsChangedEventArgs>? Changed;

    public TextSegmentationOptions GetCurrent() => Current.ToTextSegmentationOptions();

    public Task<string> GetCurrentTemplateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Current.BookFileNameTemplate!);
    }

    public async Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = Current;
            var next = ApplyUpdate(previous, update).Normalize();
            await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, next);
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, next));

            return next;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public void Dispose() => _mutex.Dispose();

    private static AppSettings ApplyUpdate(AppSettings current, AppSettingsUpdate update)
    {
        return current with
        {
            EnableLongParagraphSplitting = update.EnableLongParagraphSplitting ?? current.EnableLongParagraphSplitting,
            LongParagraphThreshold = update.LongParagraphThreshold ?? current.LongParagraphThreshold,
            SplitChaptersOnBlankLines = update.SplitChaptersOnBlankLines ?? current.SplitChaptersOnBlankLines,
            DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? current.DefaultSpeakSpeed,
            PrefetchCount = update.PrefetchCount ?? current.PrefetchCount,
            LogLevel = update.LogLevel ?? current.LogLevel,
            Theme = update.Theme ?? current.Theme,
            BookFileNameTemplate = update.BookFileNameTemplate ?? current.BookFileNameTemplate,
            CacheLimitBytes = update.CacheLimitBytes ?? current.CacheLimitBytes,
            PlaybackVolume = update.PlaybackVolume ?? current.PlaybackVolume,
            EnablePerformanceTelemetry = update.EnablePerformanceTelemetry ?? current.EnablePerformanceTelemetry,
            EnabledExperimentalFeatureIds = ApplyExperimentalFeatureChange(current, update),
            CurrentProviderId = update.ClearCurrentProvider ? null : update.CurrentProviderId ?? current.CurrentProviderId,
            MainWindowCloseBehavior = update.MainWindowCloseBehavior ?? current.MainWindowCloseBehavior,
            StartMinimizedToTray = update.StartMinimizedToTray ?? current.StartMinimizedToTray,
            ReadChapterTitle = update.ReadChapterTitle ?? current.ReadChapterTitle,
            MiniPlayerLeft = update.ClearMiniPlayerLeft ? null : update.MiniPlayerLeft ?? current.MiniPlayerLeft,
            MiniPlayerTop = update.ClearMiniPlayerTop ? null : update.MiniPlayerTop ?? current.MiniPlayerTop,
            MiniPlayerTopmost = update.MiniPlayerTopmost ?? current.MiniPlayerTopmost
        };
    }

    private static IReadOnlyList<string>? ApplyExperimentalFeatureChange(AppSettings current, AppSettingsUpdate update)
    {
        var ids = update.EnabledExperimentalFeatureIds ?? current.EnabledExperimentalFeatureIds;
        if (update.ExperimentalFeatureChange is not { } change) return ids;
        ArgumentException.ThrowIfNullOrWhiteSpace(change.FeatureId);
        var enabled = (ids ?? []).ToHashSet(StringComparer.Ordinal);
        if (change.Enabled) enabled.Add(change.FeatureId);
        else enabled.Remove(change.FeatureId);
        return enabled.ToArray();
    }
}
