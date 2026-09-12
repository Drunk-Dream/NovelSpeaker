namespace NovelSpeaker.App.Features.Diagnostics;

internal sealed class DiagnosticToolLauncher : IDiagnosticToolLauncher
{
    private readonly Func<DiagnosticToolViewModel> _viewModelFactory;
    private DiagnosticToolWindow? _window;

    public DiagnosticToolLauncher(Func<DiagnosticToolViewModel> viewModelFactory)
    {
        _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
    }

    public void Open()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var window = new DiagnosticToolWindow(_viewModelFactory());
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
