using System.Globalization;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.Playback.Presentation;

/// <summary>
/// Coordinates the playback page's rule query and global speak-speed persistence.
/// It does not own playback session state.
/// </summary>
internal sealed class PlayerRulesAndSpeedController : IDisposable
{
    private static readonly TimeSpan SpeakSpeedStepDebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly IPlaybackSession _playbackSession;
    private readonly ITtsRuleQueries _ruleQueries;
    private readonly IAppSettingsService _settingsService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly TimeProvider _timeProvider;

    private CancellationTokenSource? _speakSpeedStepDebounceCts;

    public PlayerRulesAndSpeedController(
        IPlaybackSession playbackSession,
        ITtsRuleQueries ruleQueries,
        IAppSettingsService settingsService,
        IAppFeedbackService feedbackService,
        TimeProvider timeProvider)
    {
        _playbackSession = playbackSession;
        _ruleQueries = ruleQueries;
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

    public async Task<IReadOnlyList<PlayerRuleItemViewModel>> LoadRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _ruleQueries.GetRulesAsync(cancellationToken);
        return rules
            .Select(rule => new PlayerRuleItemViewModel(rule.Id, rule.Name, rule.IsEnabled, rule.IsSelected))
            .ToArray();
    }

    public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken)
    {
        return _playbackSession.ChangeRuleAsync(ruleId, cancellationToken);
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
            _feedbackService.ShowProjectedNotification("更新语速失败", projected);
        }
    }

    public void ScheduleSpeakSpeedChange(int speakSpeed)
    {
        CancelPendingSpeakSpeedChange();
        _speakSpeedStepDebounceCts = new CancellationTokenSource();
        _ = ApplyDebouncedSpeakSpeedChangeAsync(speakSpeed, _speakSpeedStepDebounceCts.Token);
    }

    public void CancelPendingSpeakSpeedChange()
    {
        _speakSpeedStepDebounceCts?.Cancel();
        _speakSpeedStepDebounceCts?.Dispose();
        _speakSpeedStepDebounceCts = null;
    }

    public void Dispose()
    {
        CancelPendingSpeakSpeedChange();
    }

    private async Task ApplyDebouncedSpeakSpeedChangeAsync(int speakSpeed, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SpeakSpeedStepDebounceDelay, _timeProvider, cancellationToken);
            await ApplySpeakSpeedAsync(speakSpeed, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
