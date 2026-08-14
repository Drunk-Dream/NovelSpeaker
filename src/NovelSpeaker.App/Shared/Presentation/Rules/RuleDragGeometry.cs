namespace NovelSpeaker.App.Shared.Presentation.Rules;

public static class RuleDragGeometry
{
    public static RuleDropPlacement ResolvePlacement(double pointerY, double targetHeight)
    {
        if (!double.IsFinite(pointerY) || !double.IsFinite(targetHeight) || targetHeight <= 0)
        {
            return RuleDropPlacement.None;
        }

        return pointerY < targetHeight / 2
            ? RuleDropPlacement.Before
            : RuleDropPlacement.After;
    }

    public static int ResolveEdgeScrollDirection(
        double pointerY,
        double viewportHeight,
        double edgeSize)
    {
        if (!double.IsFinite(pointerY) ||
            !double.IsFinite(viewportHeight) ||
            !double.IsFinite(edgeSize) ||
            viewportHeight <= 0 ||
            edgeSize <= 0)
        {
            return 0;
        }

        var effectiveEdge = Math.Min(edgeSize, viewportHeight / 2);
        if (pointerY < effectiveEdge)
        {
            return -1;
        }

        return pointerY > viewportHeight - effectiveEdge ? 1 : 0;
    }
}
