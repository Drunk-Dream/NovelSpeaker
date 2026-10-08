using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.ImportTextSettings;

public sealed partial class ImportTextSettingsViewModel : SettingsSubpageViewModelBase
{
    private const int DebounceDelayMilliseconds = 500;

    private readonly IAppSettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private bool _isLoading;
    private readonly LatestOperationSlot _thresholdSave = new();
    private readonly LatestOperationSlot _longParagraphSplitSave = new();
    private readonly LatestOperationSlot _blankLineSplitSave = new();

    public ImportTextSettingsViewModel(
        IAppSettingsService settingsService,
        IAppNavigator navigator,
        IAppFeedbackService feedbackService,
        TimeProvider? timeProvider = null)
        : base(navigator, feedbackService)
    {
        _settingsService = settingsService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [ObservableProperty]
    private bool enableLongParagraphSplitting;

    [ObservableProperty]
    private bool splitChaptersOnBlankLines;

    [ObservableProperty]
    private string longParagraphThresholdText = string.Empty;

    [ObservableProperty]
    private string longParagraphThresholdErrorText = string.Empty;

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        Activate(cancellationToken);
        _isLoading = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = _settingsService.Current;
            EnableLongParagraphSplitting = settings.EnableLongParagraphSplitting;
            SplitChaptersOnBlankLines = settings.SplitChaptersOnBlankLines;
            LongParagraphThresholdText = settings.LongParagraphThreshold.ToString();
            LongParagraphThresholdErrorText = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    public override void Deactivate()
    {
        _thresholdSave.Cancel();
        _longParagraphSplitSave.Cancel();
        _blankLineSplitSave.Cancel();
        base.Deactivate();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenChapterRulesAsync(CancellationToken cancellationToken) =>
        Navigator.NavigateAsync(AppRoutes.ChapterRules, cancellationToken);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenFileNameMetadataRulesAsync(CancellationToken cancellationToken) =>
        Navigator.NavigateAsync(AppRoutes.FileNameMetadataRules, cancellationToken);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenTextHeaderMetadataRulesAsync(CancellationToken cancellationToken) =>
        Navigator.NavigateAsync(AppRoutes.TextHeaderMetadataRules, cancellationToken);

    public Task CommitLongParagraphThresholdAsync(CancellationToken cancellationToken) =>
        RunLatestSaveAsync(_thresholdSave, "保存长段拆分阈值失败", SaveThresholdAsync, cancellationToken);

    private async Task SaveThresholdAsync(LatestOperationSlot.Operation operation)
    {
        if (!int.TryParse(LongParagraphThresholdText, out var threshold))
        {
            operation.TryCommit(() => LongParagraphThresholdErrorText = "请输入整数。");
            return;
        }

        var settings = await _settingsService.UpdateAsync(
            new AppSettingsUpdate { LongParagraphThreshold = threshold }, operation.CancellationToken);
        operation.TryCommit(() =>
        {
            _isLoading = true;
            try
            {
                LongParagraphThresholdText = settings.LongParagraphThreshold.ToString();
                LongParagraphThresholdErrorText = string.Empty;
            }
            finally { _isLoading = false; }
        });
    }

    partial void OnEnableLongParagraphSplittingChanged(bool value)
    {
        if (_isLoading) return;
        ScheduleLatestSave(_longParagraphSplitSave, "保存超长段落拆分设置失败", operation =>
            _settingsService.UpdateAsync(new AppSettingsUpdate { EnableLongParagraphSplitting = value }, operation.CancellationToken));
    }

    partial void OnSplitChaptersOnBlankLinesChanged(bool value)
    {
        if (_isLoading) return;
        ScheduleLatestSave(_blankLineSplitSave, "保存空行分章设置失败", operation =>
            _settingsService.UpdateAsync(new AppSettingsUpdate { SplitChaptersOnBlankLines = value }, operation.CancellationToken));
    }

    partial void OnLongParagraphThresholdTextChanged(string value)
    {
        if (_isLoading) return;
        ScheduleLatestSave(_thresholdSave, "保存长段拆分阈值失败", async operation =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(DebounceDelayMilliseconds), _timeProvider, operation.CancellationToken);
            if (operation.IsCurrent) await SaveThresholdAsync(operation);
        });
    }
}
