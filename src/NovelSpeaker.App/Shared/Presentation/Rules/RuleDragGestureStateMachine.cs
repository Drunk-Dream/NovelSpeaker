using System.Windows;

namespace NovelSpeaker.App.Shared.Presentation.Rules;

public sealed class RuleDragGestureStateMachine
{
    public static readonly TimeSpan DefaultHoldThreshold = TimeSpan.FromMilliseconds(300);

    private readonly TimeSpan _holdThreshold;
    private readonly double _minimumHorizontalDistance;
    private readonly double _minimumVerticalDistance;
    private Point _pressPosition;
    private long _pressedAtMilliseconds;

    public RuleDragGestureStateMachine(
        TimeSpan? holdThreshold = null,
        double minimumHorizontalDistance = 4,
        double minimumVerticalDistance = 4)
    {
        _holdThreshold = holdThreshold ?? DefaultHoldThreshold;
        _minimumHorizontalDistance = minimumHorizontalDistance;
        _minimumVerticalDistance = minimumVerticalDistance;
    }

    public bool IsPressed { get; private set; }

    public void Press(Point position, long timestampMilliseconds, bool isExcludedRegion)
    {
        Cancel();
        if (isExcludedRegion)
        {
            return;
        }

        _pressPosition = position;
        _pressedAtMilliseconds = timestampMilliseconds;
        IsPressed = true;
    }

    public bool ShouldBeginDrag(Point position, long timestampMilliseconds, bool isLeftButtonPressed)
    {
        if (!IsPressed || !isLeftButtonPressed)
        {
            Cancel();
            return false;
        }

        if (timestampMilliseconds - _pressedAtMilliseconds < _holdThreshold.TotalMilliseconds)
        {
            return false;
        }

        var movedEnough =
            Math.Abs(position.X - _pressPosition.X) >= _minimumHorizontalDistance ||
            Math.Abs(position.Y - _pressPosition.Y) >= _minimumVerticalDistance;
        if (!movedEnough)
        {
            return false;
        }

        IsPressed = false;
        return true;
    }

    public void Cancel() => IsPressed = false;
}
