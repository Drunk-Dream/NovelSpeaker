using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.GeneralSettings;

public sealed partial class GeneralSettingsViewModel : SettingsSubpageViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private bool _isLoading;
    private bool _isReverting;
    private readonly LatestOperationSlot _closeBehaviorSave = new();
    private readonly LatestOperationSlot _startMinimizedSave = new();

    public GeneralSettingsViewModel(
        IAppSettingsService settingsService,
        IAppNavigator navigator,
        IAppFeedbackService feedbackService)
        : base(navigator, feedbackService)
    {
        _settingsService = settingsService;
    }

    public IReadOnlyList<CloseBehaviorOption> CloseBehaviorOptions { get; } =
    [
        new(MainWindowCloseBehavior.MinimizeToTray, "最小化到托盘"),
        new(MainWindowCloseBehavior.ExitApplication, "退出应用"),
        new(MainWindowCloseBehavior.AskEveryTime, "每次询问")
    ];

    [ObservableProperty]
    private CloseBehaviorOption? selectedCloseBehavior;

    [ObservableProperty]
    private bool startMinimizedToTray;

    public override Task LoadAsync(CancellationToken cancellationToken)
    {
        Activate(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _isLoading = true;
        try
        {
            ApplySettings(_settingsService.Current);
        }
        finally
        {
            _isLoading = false;
        }

        return Task.CompletedTask;
    }

    partial void OnSelectedCloseBehaviorChanged(CloseBehaviorOption? value)
    {
        if (_isLoading || _isReverting || value is null)
        {
            return;
        }

        ScheduleLatestSave(_closeBehaviorSave, "保存关闭行为失败", async operation =>
        {
            try
            {
                var settings = await _settingsService.UpdateAsync(
                    new AppSettingsUpdate { MainWindowCloseBehavior = value.Value }, operation.CancellationToken);
                operation.TryCommit(() => ApplyCloseBehavior(settings.MainWindowCloseBehavior));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                operation.TryCommit(() => ApplyCloseBehavior(_settingsService.Current.MainWindowCloseBehavior));
                throw;
            }
        });
    }

    partial void OnStartMinimizedToTrayChanged(bool value)
    {
        if (_isLoading || _isReverting) return;
        ScheduleLatestSave(_startMinimizedSave, "保存启动行为失败", async operation =>
        {
            try
            {
                var settings = await _settingsService.UpdateAsync(
                    new AppSettingsUpdate { StartMinimizedToTray = value }, operation.CancellationToken);
                operation.TryCommit(() => ApplyStartMinimized(settings.StartMinimizedToTray));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                operation.TryCommit(() => ApplyStartMinimized(_settingsService.Current.StartMinimizedToTray));
                throw;
            }
        });
    }

    public override void Deactivate()
    {
        _closeBehaviorSave.Cancel();
        _startMinimizedSave.Cancel();
        base.Deactivate();
    }

    private void ApplySettings(AppSettings settings)
    {
        ApplyCloseBehavior(settings.MainWindowCloseBehavior);
        ApplyStartMinimized(settings.StartMinimizedToTray);
    }

    private void ApplyCloseBehavior(MainWindowCloseBehavior value)
    {
        _isReverting = true;
        SelectedCloseBehavior = CloseBehaviorOptions.Single(option => option.Value == value);
        _isReverting = false;
    }

    private void ApplyStartMinimized(bool value)
    {
        _isReverting = true;
        StartMinimizedToTray = value;
        _isReverting = false;
    }
}
