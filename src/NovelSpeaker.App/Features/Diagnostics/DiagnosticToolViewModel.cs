using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Features.Diagnostics;

public sealed partial class DiagnosticToolViewModel : ObservableObject
{
    private static readonly IReadOnlyList<CapacityOption> CapacityChoices =
    [
        new(DiagnosticSessionCapacityPresets.SmallBytes, "16 MB"),
        new(DiagnosticSessionCapacityPresets.StandardBytes, "64 MB"),
        new(DiagnosticSessionCapacityPresets.LargeBytes, "256 MB")
    ];

    private readonly DiagnosticRecordingController _recording;
    private readonly IDiagnosticSessionExportService _exports;
    private readonly IDiagnosticWindowCapture _windowCapture;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly TimeProvider _timeProvider;
    private DiagnosticSessionSnapshot? _sessionSnapshot;
    private DateTimeOffset? _startedAtUtc;

    internal DiagnosticToolViewModel(
        DiagnosticRecordingController recording,
        IDiagnosticSessionExportService exports,
        IDiagnosticWindowCapture windowCapture,
        IPresentationFileDialogService fileDialogs,
        TimeProvider timeProvider)
    {
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        _exports = exports ?? throw new ArgumentNullException(nameof(exports));
        _windowCapture = windowCapture ?? throw new ArgumentNullException(nameof(windowCapture));
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        SelectedCapacity = CapacityChoices[1];
        RefreshFromSession();
    }

    public IReadOnlyList<CapacityOption> CapacityOptions => CapacityChoices;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkProblemCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureWindowCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private DiagnosticToolState state = DiagnosticToolState.Preparing;

    [ObservableProperty]
    private CapacityOption selectedCapacity;

    [ObservableProperty]
    private string stateText = string.Empty;

    [ObservableProperty]
    private string sessionStorageText = string.Empty;

    [ObservableProperty]
    private string droppedRecordText = string.Empty;

    [ObservableProperty]
    private string durationText = "00:00:00";

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

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanStart))]
    private Task StartAsync(CancellationToken cancellationToken) =>
        StartSessionAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanRestart))]
    private Task RestartAsync(CancellationToken cancellationToken) =>
        StartSessionAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanEnd))]
    private Task EndAsync(CancellationToken cancellationToken) => RunCommandAsync(
        async () => ApplySnapshot(await _recording.EndAsync(cancellationToken)),
        "无法结束诊断会话。",
        cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanCapture))]
    private Task CaptureWindowAsync(CancellationToken cancellationToken) => RunCommandAsync(
        async () =>
        {
            var attachment = await _windowCapture.CaptureCurrentWindowAsync(cancellationToken);
            await _recording.AddAttachmentAsync(attachment, cancellationToken);
            RefreshFromSession();
        },
        "无法截取当前窗口。",
        cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanCapture))]
    private Task MarkProblemAsync(CancellationToken cancellationToken) => RunCommandAsync(
        async () =>
        {
            if (!await _recording.RecordProblemMarkerAsync(cancellationToken))
            {
                ErrorText = "问题标记未能写入诊断会话。";
                return;
            }

            RefreshFromSession();
        },
        "无法记录问题标记。",
        cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanExport))]
    private Task ExportAsync(CancellationToken cancellationToken) => RunCommandAsync(
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
        },
        "无法导出问题诊断包。",
        cancellationToken);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void RefreshDuration()
    {
        RefreshFromSession();

        if (_startedAtUtc is null || State != DiagnosticToolState.Capturing)
        {
            return;
        }

        DurationText = FormatDuration(_timeProvider.GetUtcNow() - _startedAtUtc.Value);
    }

    private Task StartSessionAsync(CancellationToken cancellationToken) => RunCommandAsync(
        async () => ApplySnapshot(await _recording.StartAsync(
            SelectedCapacity.Bytes,
            cancellationToken)),
        "无法开始诊断会话。",
        cancellationToken);

    private bool CanStart() => State == DiagnosticToolState.Preparing && _recording.Snapshot is null;

    private bool CanRestart() => State == DiagnosticToolState.Completed && _recording.Snapshot?.State != DiagnosticSessionState.Active;

    private bool CanEnd() => State == DiagnosticToolState.Capturing && _sessionSnapshot?.State == DiagnosticSessionState.Active;

    private bool CanCapture() =>
        State == DiagnosticToolState.Capturing && !IsCaptureStopped && _sessionSnapshot?.State == DiagnosticSessionState.Active;

    private bool CanExport() => State == DiagnosticToolState.Completed && _sessionSnapshot?.State == DiagnosticSessionState.Ended;

    private void RefreshFromSession()
    {
        var snapshot = _recording.Snapshot;
        if (snapshot is null)
        {
            _sessionSnapshot = null;
            State = DiagnosticToolState.Preparing;
            IsCaptureStopped = false;
            SessionStorageText = string.Empty;
            DroppedRecordText = string.Empty;
            StateText = "准备开始诊断。";
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(DiagnosticSessionSnapshot snapshot)
    {
        _sessionSnapshot = snapshot;
        _startedAtUtc = snapshot.StartedAtUtc;
        SelectedCapacity = CapacityChoices.FirstOrDefault(option => option.Bytes == snapshot.HardCapBytes)
            ?? CapacityChoices[1];
        SessionStorageText = $"{FormatMegabytes(snapshot.RecordedBytes)} / {FormatMegabytes(snapshot.HardCapBytes)}";
        DroppedRecordText = snapshot.CurrentProcessDroppedRecordCount == 0
            ? "本进程无队列丢弃"
            : $"本进程队列已跳过 {snapshot.CurrentProcessDroppedRecordCount.ToString("N0", CultureInfo.CurrentCulture)} 条记录";
        IsCaptureStopped = snapshot.CaptureStopped;
        State = snapshot.State switch
        {
            DiagnosticSessionState.Active => DiagnosticToolState.Capturing,
            DiagnosticSessionState.Ended => DiagnosticToolState.Completed,
            _ => DiagnosticToolState.Preparing
        };

        StateText = snapshot.State switch
        {
            DiagnosticSessionState.Active when snapshot.CaptureStopped =>
                GetCaptureStoppedText(snapshot.CaptureStoppedReason),
            DiagnosticSessionState.Active => "诊断中",
            DiagnosticSessionState.Ended => "诊断已保存",
            _ => "准备开始诊断。"
        };

        OnPropertyChanged(nameof(SessionStorageText));
    }

    private async Task RunCommandAsync(
        Func<Task> operation,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        ErrorText = string.Empty;
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            ErrorText = failureMessage;
        }
    }

    private static string GetCaptureStoppedText(string? reason) => reason switch
    {
        "hard-cap" => "已达到容量上限，采集已停止；仍可结束并保存。",
        "storage-failure" => "诊断存储遇到问题，采集已停止；仍可结束并保存。",
        _ => "采集已停止；仍可结束并保存。"
    };

    private static string FormatMegabytes(long bytes)
    {
        var megabytes = Math.Max(0, bytes) / (1024d * 1024d);
        return $"{megabytes.ToString("0.#", CultureInfo.CurrentCulture)} MB";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = Math.Max(0, (int)duration.TotalHours);
        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    public sealed record CapacityOption(long Bytes, string DisplayText);
}
