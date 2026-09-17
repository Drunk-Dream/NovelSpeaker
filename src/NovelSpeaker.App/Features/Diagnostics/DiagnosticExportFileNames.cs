using System.Globalization;

namespace NovelSpeaker.App.Features.Diagnostics;

internal static class DiagnosticExportFileNames
{
    public static string Diagnostics(TimeProvider timeProvider) =>
        Create("NovelSpeaker-Diagnostics", timeProvider);

    public static string ProblemDiagnostics(TimeProvider timeProvider) =>
        Create("NovelSpeaker-Problem-Diagnostics", timeProvider);

    private static string Create(string prefix, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var timestamp = timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return $"{prefix}-{timestamp}.zip";
    }
}
