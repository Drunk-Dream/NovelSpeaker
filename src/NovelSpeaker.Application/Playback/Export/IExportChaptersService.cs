namespace NovelSpeaker.Application.Playback.Export;

/// <summary>
/// Exports complete current-configuration chapter caches without generating missing audio.
/// Cancellation stops remaining work and propagates as normal control flow.
/// </summary>
public interface IExportChaptersService
{
    Task<ExportChaptersResult> ExportAsync(
        ExportChaptersRequest request,
        CancellationToken cancellationToken);
}
