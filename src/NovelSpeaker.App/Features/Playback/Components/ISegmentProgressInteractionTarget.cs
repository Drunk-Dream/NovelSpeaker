namespace NovelSpeaker.App.Features.Playback.Components;

/// <summary>
/// Provides the presentation state and operations shared by playback progress sliders.
/// </summary>
public interface ISegmentProgressInteractionTarget
{
    bool IsSegmentProgressDragging { get; }

    void BeginSegmentProgressInteraction();

    void PreviewSegmentProgress(double value);

    Task CommitSegmentProgressAsync(double value, CancellationToken cancellationToken);
}
