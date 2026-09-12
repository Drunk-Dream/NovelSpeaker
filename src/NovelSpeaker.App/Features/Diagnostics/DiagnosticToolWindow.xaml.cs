using System.Windows.Threading;

namespace NovelSpeaker.App.Features.Diagnostics;

public partial class DiagnosticToolWindow : System.Windows.Window
{
    private readonly DispatcherTimer _durationTimer;

    public DiagnosticToolWindow(DiagnosticToolViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
        _durationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _durationTimer.Tick += OnDurationTimerTick;
        _durationTimer.Start();
        ViewModel.CloseRequested += OnCloseRequested;
        Closing += OnClosing;
    }

    public DiagnosticToolViewModel ViewModel { get; }

    protected override void OnClosed(EventArgs e)
    {
        _durationTimer.Stop();
        _durationTimer.Tick -= OnDurationTimerTick;
        ViewModel.CloseRequested -= OnCloseRequested;
        Closing -= OnClosing;
        base.OnClosed(e);
    }

    private void OnDurationTimerTick(object? sender, EventArgs e) => ViewModel.RefreshDuration();

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ViewModel.IsCapturing || (System.Windows.Application.Current?.Dispatcher.HasShutdownStarted ?? false))
        {
            return;
        }

        e.Cancel = true;
        System.Windows.MessageBox.Show(
            this,
            "请先结束诊断会话，再关闭问题诊断工具。",
            "问题诊断",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }
}
