using NovelSpeaker.Domain.Speech.Providers;
using System.Globalization;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.App.Shell.Activation;

namespace NovelSpeaker.App.Features.Playback.Presentation;

/// <summary>
/// Coordinates the playback page's provider query and global speak-speed persistence.
/// It does not own playback session state.
/// </summary>
internal sealed class PlayerSpeechControlController : IDisposable
{
    private static readonly TimeSpan SpeakSpeedStepDebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly IPlaybackSession _playbackSession;
    private readonly ICurrentSpeechProvider _providers;
    private readonly IAppSettingsService _settingsService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly TimeProvider _timeProvider;
    private readonly LatestOperationSlot _speakSpeedSave = new();
    private PageActivationScope? _activation;

    public PlayerSpeechControlController(
        IPlaybackSession playbackSession,
        ICurrentSpeechProvider providers,
        IAppSettingsService settingsService,
        IAppFeedbackService feedbackService,
        TimeProvider timeProvider)
    {
        _playbackSession = playbackSession;
        _providers = providers;
        _settingsService = settingsService;
        _feedbackService = feedbackService;
        _timeProvider = timeProvider;
        DefaultSpeakSpeed = settingsService.Current.DefaultSpeakSpeed;
    }

    public int DefaultSpeakSpeed { get; private set; }

    public void RefreshDefaultSpeakSpeed()
    {
        DefaultSpeakSpeed = _settingsService.Current.DefaultSpeakSpeed;
    }

    public async Task<IReadOnlyList<PlayerProviderItemViewModel>> LoadProvidersAsync(CancellationToken cancellationToken)
    {
        var providers = await _providers.GetAvailableAsync(cancellationToken);
        return providers
            .Select(provider => new PlayerProviderItemViewModel(provider.Id, provider.Name, provider.Id == _settingsService.Current.CurrentProviderId))
            .ToArray();
    }

    public Task ChangeProviderAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        return _playbackSession.ChangeProviderAsync(providerId, cancellationToken);
    }

    public bool TryParseSpeakSpeed(string text, out int parsedSpeed, out string errorText)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSpeed) ||
            !AppSettings.IsValidSpeakSpeed(parsedSpeed))
        {
            errorText = $"请输入 {AppSettings.MinSpeakSpeed} 到 {AppSettings.MaxSpeakSpeed} 的整数。";
            return false;
        }

        errorText = string.Empty;
        return true;
    }

    public int ResolvePendingSpeakSpeed(string editorText, int currentSpeakSpeed)
    {
        return int.TryParse(editorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSpeed) &&
               AppSettings.IsValidSpeakSpeed(parsedSpeed)
            ? parsedSpeed
            : currentSpeakSpeed;
    }

    public async Task ApplySpeakSpeedAsync(int speakSpeed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (AppSettings.NormalizeSpeakSpeed(_playbackSession.CurrentSnapshot.SpeakSpeed) == speakSpeed &&
            _settingsService.Current.DefaultSpeakSpeed == speakSpeed)
        {
            return;
        }

        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    DefaultSpeakSpeed = speakSpeed
                },
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            DefaultSpeakSpeed = settings.DefaultSpeakSpeed;

            if (!string.IsNullOrWhiteSpace(_playbackSession.CurrentSnapshot.BookId) &&
                _playbackSession.CurrentSnapshot.SpeakSpeed != settings.DefaultSpeakSpeed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _playbackSession.ChangeSpeedAsync(settings.DefaultSpeakSpeed, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            if (!cancellationToken.IsCancellationRequested)
                _feedbackService.ShowProjectedNotification("更新语速失败", projected);
        }
    }

    public void Activate(PageActivationScope activation) => _activation = activation;

    public void Deactivate()
    {
        _activation = null;
        CancelPendingSpeakSpeedChange();
    }

    public void ScheduleSpeakSpeedChange(int speakSpeed)
    {
        if (_activation is not { IsCurrent: true } activation) return;
        var operation = _speakSpeedSave.Begin(activation: activation);
        activation.Register(operation.RunAsync(async current =>
        {
            await Task.Delay(SpeakSpeedStepDebounceDelay, _timeProvider, current.CancellationToken);
            if (current.IsCurrent) await ApplySpeakSpeedAsync(speakSpeed, current.CancellationToken);
        }, exception => _feedbackService.ShowProjectedNotification("更新语速失败", _feedbackService.Project(exception))));
    }

    public void CancelPendingSpeakSpeedChange() => _speakSpeedSave.Cancel();

    public void Dispose() => Deactivate();
}
