namespace NovelSpeaker.Application.Diagnostics;

/// <summary>
/// Small user-facing choices for diagnostic session hard caps.
/// </summary>
public static class DiagnosticSessionCapacityPresets
{
    public const long MinimumSupportedBytes = 1L * 1024 * 1024;
    public const long SmallBytes = 16L * 1024 * 1024;
    public const long StandardBytes = 64L * 1024 * 1024;
    public const long LargeBytes = 256L * 1024 * 1024;

    public static IReadOnlyList<long> All { get; } = [SmallBytes, StandardBytes, LargeBytes];

    public static long DefaultBytes => StandardBytes;

    public static bool IsSupported(long value) => All.Contains(value);
}
