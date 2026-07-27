using System.ComponentModel;
using System.Windows;

namespace NovelSpeaker.App.Desktop.MiniPlayer;

public partial class MiniPlayerWindow : Window
{
    private bool _allowClose;
    private bool _placementApplied;

    private readonly IMiniPlayerScreenBoundsProvider _screenBoundsProvider;

    public MiniPlayerWindow(
        MiniPlayerViewModel viewModel,
        IMiniPlayerScreenBoundsProvider screenBoundsProvider)
    {
        ViewModel = viewModel;
        _screenBoundsProvider = screenBoundsProvider;
        DataContext = viewModel;
        InitializeComponent();
        ViewModel.RestoreRequested += OnRestoreRequested;
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        Closing += OnClosing;
    }

    public MiniPlayerViewModel ViewModel { get; }

    public event EventHandler? RestoreRequested;

    internal void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_placementApplied)
        {
            return;
        }

        _placementApplied = true;
        if (MiniPlayerPlacementValidator.TryValidate(
                ViewModel.SavedLeft,
                ViewModel.SavedTop,
                ActualWidth > 0 ? ActualWidth : Width,
                ActualHeight > 0 ? ActualHeight : Height,
                _screenBoundsProvider.GetWorkAreas(),
                out var placement))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_placementApplied && IsVisible && WindowState == WindowState.Normal)
        {
            ViewModel.UpdateWindowPosition(Left, Top);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            ViewModel.RestoreRequested -= OnRestoreRequested;
            return;
        }

        e.Cancel = true;
        ViewModel.RequestRestore();
    }

    private void OnRestoreRequested(object? sender, EventArgs e) =>
        RestoreRequested?.Invoke(this, EventArgs.Empty);
}

internal readonly record struct MiniPlayerPlacement(double Left, double Top);

internal static class MiniPlayerPlacementValidator
{
    public static bool TryValidate(
        double? left,
        double? top,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<MiniPlayerScreenBounds> workAreas,
        out MiniPlayerPlacement placement)
    {
        placement = default;
        if (left is not { } x ||
            top is not { } y ||
            !double.IsFinite(x) ||
            !double.IsFinite(y) ||
            !double.IsFinite(windowWidth) ||
            !double.IsFinite(windowHeight) ||
            windowWidth <= 0 ||
            windowHeight <= 0 ||
            workAreas is null)
        {
            return false;
        }

        foreach (var bounds in workAreas)
        {
            if (!double.IsFinite(bounds.Left) ||
                !double.IsFinite(bounds.Top) ||
                !double.IsFinite(bounds.Width) ||
                !double.IsFinite(bounds.Height) ||
                bounds.Width <= 0 ||
                bounds.Height <= 0)
            {
                continue;
            }

            var right = bounds.Left + bounds.Width;
            var bottom = bounds.Top + bounds.Height;
            if (x >= bounds.Left &&
                y >= bounds.Top &&
                x + Math.Min(windowWidth, bounds.Width) <= right &&
                y + Math.Min(windowHeight, bounds.Height) <= bottom)
            {
                placement = new MiniPlayerPlacement(x, y);
                return true;
            }
        }

        return false;
    }
}
