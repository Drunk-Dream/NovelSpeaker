namespace NovelSpeaker.App.Diagnostics;

/// <summary>
/// Provides the platform clipboard boundary used by view models.
/// </summary>
public interface IClipboardService
{
    void SetText(string text);
}
