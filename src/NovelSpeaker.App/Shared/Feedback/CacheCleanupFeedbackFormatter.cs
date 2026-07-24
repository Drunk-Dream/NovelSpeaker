using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.App.Shared.Feedback;

internal static class CacheCleanupFeedbackFormatter
{
    public static (string Title, string Message, bool IsWarning) Format(
        CacheCleanupResult result,
        string successTitle,
        string warningTitle)
    {
        var deletedText = $"已清理 {result.DeletedEntryCount} 项，释放 {FormatBytes(result.DeletedBytes)}。";
        if (!result.HasWarnings)
        {
            return (successTitle, deletedText, false);
        }

        var parts = new List<string> { deletedText.TrimEnd('。') };
        if (result.ProtectedEntryCount > 0)
        {
            parts.Add($"保留 {result.ProtectedEntryCount} 项");
        }

        if (result.FailedEntryCount > 0)
        {
            parts.Add($"失败 {result.FailedEntryCount} 项");
        }

        return (warningTitle, string.Join("；", parts) + "。", true);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var size = bytes / 1024d;
        var units = new[] { "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024d;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }
}
