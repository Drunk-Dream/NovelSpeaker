using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NovelSpeaker.App.Features.Playback.Components;

namespace NovelSpeaker.App.Desktop.MiniPlayer;

public partial class MiniPlayerWindow : Window
{
    private bool _allowClose;
    private bool _placementApplied;

    private readonly IMiniPlayerScreenBoundsProvider _screenBoundsProvider;
    private readonly PlayerProgressInteractionController _progressController;

    public MiniPlayerWindow(
        MiniPlayerViewModel viewModel,
        IMiniPlayerScreenBoundsProvider screenBoundsProvider)
    {
        ViewModel = viewModel;
        _screenBoundsProvider = screenBoundsProvider;
        DataContext = viewModel;
        _progressController = new PlayerProgressInteractionController(
            () => ViewModel,
            () => ViewModel.LifetimeCancellationToken);
        InitializeComponent();
        ViewModel.RestoreRequested += OnRestoreRequested;
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        Closing += OnClosing;
    }

    public MiniPlayerViewModel ViewModel { get; }

    public event EventHandler? RestoreRequested;

    public event EventHandler? ExitRequested;

    internal void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void MiniPlayerWindow_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!MiniPlayerWindowDragPolicy.CanStartDrag(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // The window may have lost its mouse capture while the drag was starting.
        }
    }

    private void MiniPlayerWidthResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        if (!double.IsFinite(currentWidth))
        {
            return;
        }

        Width = Math.Clamp(currentWidth + e.HorizontalChange, MinWidth, MaxWidth);
        e.Handled = true;
    }

    private void MiniPlayerProgressSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _progressController.Preview(e.NewValue);
    }

    private void MiniPlayerProgressSlider_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Slider slider)
        {
            _progressController.OnMouseEnter(slider);
        }
    }

    private void MiniPlayerProgressSlider_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Slider slider)
        {
            _progressController.OnMouseLeave(slider);
        }
    }

    private void MiniPlayerProgressSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            _progressController.BeginMouse(slider);
        }
    }

    private async void MiniPlayerProgressSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider)
        {
            await RunProgressOperationAsync(
                () => _progressController.CommitMouseAsync(slider));
        }
    }

    private void MiniPlayerProgressSlider_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _progressController.BeginKeyboard((Slider)sender, e.Key);
    }

    private async void MiniPlayerProgressSlider_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        await RunProgressOperationAsync(
            () => _progressController.CommitKeyboardAsync(slider, e.Key));
    }

    private async void MiniPlayerProgressSlider_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is Slider slider)
        {
            await RunProgressOperationAsync(
                () => _progressController.CommitMouseAsync(slider));
        }
    }

    private async Task RunProgressOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (ViewModel.LifetimeCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ViewModel.ReportProgressOperationFailure(exception);
        }
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
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MiniPlayerCloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void OnRestoreRequested(object? sender, EventArgs e) =>
        RestoreRequested?.Invoke(this, EventArgs.Empty);
}

internal static class MiniPlayerWindowDragPolicy
{
    public static bool CanStartDrag(DependencyObject? source)
    {
        return source is not null &&
               FindAncestor<ButtonBase>(source) is null &&
               FindAncestor<Thumb>(source) is null &&
               FindAncestor<Slider>(source) is null &&
               FindAncestor<TextBoxBase>(source) is null;
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
    }
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
