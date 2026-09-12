using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Features.Diagnostics;

public sealed partial class DiagnosticToolViewModel : ObservableObject
{
    private readonly IDiagnosticSessionService _sessions;
    private readonly IDiagnosticSessionExportService _exports;
    private readonly IDiagnosticWindowCapture _windowCapture;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset? _startedAtUtc;
    private string? _endedSessionId;

    public DiagnosticToolViewModel(
        IDiagnosticSessionService sessions,
        IDiagnosticSessionExportService exports,
        IDiagnosticWindowCapture windowCapture,
        IPresentationFileDialogService fileDialogs,
        TimeProvider timeProvider)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _exports = exports ?? throw new ArgumentNullException(nameof(exports));
        _windowCapture = windowCapture ?? throw new ArgumentNullException(nameof(windowCapture));
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        SelectedHardCapBytes = DiagnosticSessionCapacityPresets.DefaultBytes;
        StateText = "准备开始诊断。打开工具不会开始采集。";
        RefreshFromSession();
    }

    public IReadOnlyList<long> CapacityOptions => DiagnosticSessionCapacityPresets.All;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkProblemCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureWindowCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private DiagnosticToolState state = DiagnosticToolState.Preparing;

    [ObservableProperty]
    private long selectedHardCapBytes;

    [ObservableProperty]
    private string stateText = string.Empty;

    [ObservableProperty]
    private string durationText = "00:00:00";

    [ObservableProperty]
    private int markerCount;

    [ObservableProperty]
    private int attachmentCount;

    public string CaptureCountText => $"问题标记：{MarkerCount}；截图：{AttachmentCount}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MarkProblemCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureWindowCommand))]
    private bool isCaptureStopped;

    [ObservableProperty]
    private string errorText = string.Empty;

    public bool IsPreparing => State == DiagnosticToolState.Preparing;
    public bool IsCapturing => State == DiagnosticToolState.Capturing;
    public bool IsCompleted => State == DiagnosticToolState.Completed;

    public event EventHandler? CloseRequested;

    partial void OnStateChanged(DiagnosticToolState value)
    {
        OnPropertyChanged(nameof(IsPreparing));
        OnPropertyChanged(nameof(IsCapturing));
        OnPropertyChanged(nameof(IsCompleted));
    }

    partial void OnSelectedHardCapBytesChanged(long value)
    {
        if (!DiagnosticSessionCapacityPresets.All.Contains(value))
        {
            SelectedHardCapBytes = DiagnosticSessionCapacityPresets.DefaultBytes;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanStart))]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var snapshot = await _sessions.StartAsync(
                    new DiagnosticSessionStartOptions(SelectedHardCapBytes),
                    cancellationToken);
                _startedAtUtc = snapshot.StartedAtUtc;
                _endedSessionId = null;
                MarkerCount = 0;
                AttachmentCount = 0;
                OnPropertyChanged(nameof(CaptureCountText));
                State = DiagnosticToolState.Capturing;
                ErrorText = string.Empty;
                ApplySnapshot(snapshot);
            });
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanRestart))]
    private async Task RestartAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var snapshot = await _sessions.StartAsync(
                    new DiagnosticSessionStartOptions(SelectedHardCapBytes),
                    cancellationToken);
                _startedAtUtc = snapshot.StartedAtUtc;
                _endedSessionId = null;
                MarkerCount = 0;
                AttachmentCount = 0;
                OnPropertyChanged(nameof(CaptureCountText));
                State = DiagnosticToolState.Capturing;
                ErrorText = string.Empty;
                ApplySnapshot(snapshot);
            });
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanEnd))]
    private async Task EndAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var snapshot = await _sessions.EndAsync(cancellationToken);
                _endedSessionId = snapshot.SessionId;
                State = DiagnosticToolState.Completed;
                ApplySnapshot(snapshot);
                StateText = "诊断已保存，可以导出或重新开始。";
            });
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanCapture))]
    private async Task CaptureWindowAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var attachment = await _windowCapture
                    .CaptureCurrentWindowAsync(cancellationToken);
                await _sessions.AddAttachmentAsync(attachment, cancellationToken);
                AttachmentCount++;
                OnPropertyChanged(nameof(CaptureCountText));
                RefreshFromSession();
            });
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanCapture))]
    private Task MarkProblemAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.RecordProblemMarker();
        MarkerCount++;
        OnPropertyChanged(nameof(CaptureCountText));
        RefreshFromSession();
        return Task.CompletedTask;
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanExport))]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var destinationPath = await _fileDialogs.PickSaveFileAsync(
                    new PresentationFileDialogOptions(
                        "ZIP files (*.zip)|*.zip",
                        "NovelSpeaker-Problem-Diagnostics.zip"),
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    return;
                }

                await _exports.ExportLastEndedAsync(destinationPath, cancellationToken);
                StateText = "问题诊断包已导出。";
            });
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void RefreshDuration()
    {
        RefreshFromSession();

        if (_startedAtUtc is null || State != DiagnosticToolState.Capturing)
        {
            return;
        }

        var elapsed = _timeProvider.GetUtcNow() - _startedAtUtc.Value;
        DurationText = FormatDuration(elapsed);
    }

    private bool CanStart() => State == DiagnosticToolState.Preparing && _sessions.Current is null;

    private bool CanRestart() => State == DiagnosticToolState.Completed && _sessions.Current is null;

    private bool CanEnd() => State == DiagnosticToolState.Capturing && _sessions.Current is not null;

    private bool CanCapture() =>
        State == DiagnosticToolState.Capturing && !IsCaptureStopped && _sessions.Current is not null;

    private bool CanExport() => State == DiagnosticToolState.Completed && !string.IsNullOrWhiteSpace(_endedSessionId);

    private void RefreshFromSession()
    {
        var snapshot = _sessions.Current;
        if (snapshot is not null)
        {
            if (snapshot.State == DiagnosticSessionState.Active && State == DiagnosticToolState.Preparing)
            {
                _startedAtUtc = snapshot.StartedAtUtc;
                State = DiagnosticToolState.Capturing;
            }

            ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(DiagnosticSessionSnapshot snapshot)
    {
        IsCaptureStopped = snapshot.CaptureStopped;
        if (snapshot.CaptureStopped)
        {
            StateText = "已达到容量上限，采集已停止；仍可结束并保存当前会话。";
        }
        else if (snapshot.State == DiagnosticSessionState.Active)
        {
            StateText = "诊断中：请复现问题，可按需标记或截取当前窗口。";
        }
    }

    private async Task RunAsync(Func<Task> operation)
    {
        ErrorText = string.Empty;
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = Math.Max(0, (int)duration.TotalHours);
        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
