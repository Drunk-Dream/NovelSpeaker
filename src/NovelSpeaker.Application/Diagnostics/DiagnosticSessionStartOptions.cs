namespace NovelSpeaker.Application.Diagnostics;

public sealed record DiagnosticSessionStartOptions
{
    public DiagnosticSessionStartOptions(long hardCapBytes = DiagnosticSessionCapacityPresets.StandardBytes)
    {
        if (hardCapBytes < DiagnosticSessionCapacityPresets.MinimumSupportedBytes ||
            hardCapBytes > 512L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(hardCapBytes));
        }

        HardCapBytes = hardCapBytes;
    }

    public long HardCapBytes { get; }
}
