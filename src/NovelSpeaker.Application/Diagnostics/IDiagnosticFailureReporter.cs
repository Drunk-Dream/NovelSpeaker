namespace NovelSpeaker.Application.Diagnostics;

/// <summary>
/// Reports failures within user-visible diagnostic operations without affecting the operation.
/// </summary>
public interface IDiagnosticFailureReporter
{
    void ReportFailure(
        DiagnosticFailureOperation operation,
        DiagnosticFailureStage stage,
        Exception exception);
}
