using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Features.Diagnostics;

internal sealed class DiagnosticToolLauncher : IDiagnosticToolLauncher
{
    private readonly DiagnosticRecordingController _recording;
    private readonly IDiagnosticSessionExportService _exports;
    private readonly IDiagnosticWindowCapture _windowCapture;
    private readonly IPresentationFileDialogService _fileDialogs;
    private readonly TimeProvider _timeProvider;
    private DiagnosticToolWindow? _window;

    public DiagnosticToolLauncher(
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
    }

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await _recording.RecoverAsync(cancellationToken).ConfigureAwait(false);
    }

    internal DiagnosticToolWindow? CurrentWindow => _window;

    public void OpenIfSessionActive()
    {
        if (_recording.Snapshot?.State == DiagnosticSessionState.Active)
        {
            Open();
        }
    }

    public void Open()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var window = new DiagnosticToolWindow(
            new DiagnosticToolViewModel(
                _recording,
                _exports,
                _windowCapture,
                _fileDialogs,
                _timeProvider));
        _window = window;
        window.Closed += OnWindowClosed;
        window.Show();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is DiagnosticToolWindow window)
        {
            window.Closed -= OnWindowClosed;
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        }
    }
}
