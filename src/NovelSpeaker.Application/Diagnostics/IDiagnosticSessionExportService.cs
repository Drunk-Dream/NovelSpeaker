namespace NovelSpeaker.Application.Diagnostics;

/// <summary>
/// Produces the user-facing problem-diagnostics exchange package from one .nsdiag file.
/// </summary>
public interface IDiagnosticSessionExportService
{
    Task ExportLastEndedAsync(string destinationPath, CancellationToken cancellationToken);

    Task ExportAsync(
        string sessionFilePath,
        string destinationPath,
        CancellationToken cancellationToken);
}
