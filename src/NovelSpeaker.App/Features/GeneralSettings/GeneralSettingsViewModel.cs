using CommunityToolkit.Mvvm.ComponentModel;
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
    private int _closeBehaviorVersion;
    private int _startMinimizedVersion;

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

        var version = Interlocked.Increment(ref _closeBehaviorVersion);
        RunPageOperation(
            "保存关闭行为失败",
            cancellationToken => SaveCloseBehaviorAsync(value.Value, version, cancellationToken));
    }

    partial void OnStartMinimizedToTrayChanged(bool value)
    {
        if (_isLoading || _isReverting)
        {
            return;
        }

        var version = Interlocked.Increment(ref _startMinimizedVersion);
        RunPageOperation(
            "保存启动行为失败",
            cancellationToken => SaveStartBehaviorAsync(value, version, cancellationToken));
    }

    private async Task SaveCloseBehaviorAsync(
        MainWindowCloseBehavior value,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate { MainWindowCloseBehavior = value },
                cancellationToken).ConfigureAwait(true);
            if (IsCurrentActivation(cancellationToken) &&
                version == Volatile.Read(ref _closeBehaviorVersion))
            {
                ApplyCloseBehavior(settings.MainWindowCloseBehavior);
            }
        }
        catch
        {
            if (IsCurrentActivation(cancellationToken) &&
                version == Volatile.Read(ref _closeBehaviorVersion))
            {
                ApplyCloseBehavior(_settingsService.Current.MainWindowCloseBehavior);
            }

            throw;
        }
    }

    private async Task SaveStartBehaviorAsync(
        bool value,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate { StartMinimizedToTray = value },
                cancellationToken).ConfigureAwait(true);
            if (IsCurrentActivation(cancellationToken) &&
                version == Volatile.Read(ref _startMinimizedVersion))
            {
                ApplyStartMinimized(settings.StartMinimizedToTray);
            }
        }
        catch
        {
            if (IsCurrentActivation(cancellationToken) &&
                version == Volatile.Read(ref _startMinimizedVersion))
            {
                ApplyStartMinimized(_settingsService.Current.StartMinimizedToTray);
            }

            throw;
        }
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
