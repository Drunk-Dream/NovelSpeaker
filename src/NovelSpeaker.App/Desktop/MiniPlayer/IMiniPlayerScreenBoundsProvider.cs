namespace NovelSpeaker.App.Desktop.MiniPlayer;

public interface IMiniPlayerScreenBoundsProvider
{
    IReadOnlyList<MiniPlayerScreenBounds> GetWorkAreas();
}

public readonly record struct MiniPlayerScreenBounds(
    double Left,
    double Top,
    double Width,
    double Height);
