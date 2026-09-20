namespace NovelSpeaker.Application.Diagnostics;

/// <summary>
/// Stable diagnostic-system operations whose failures are visible to users.
/// </summary>
public enum DiagnosticFailureOperation
{
    DiagnosticsExport,
    ProblemDiagnosticsExport,
    SessionStart,
    SessionWrite,
    SessionRecover,
    SessionEnd,
    ProblemMarker,
    WindowCapture
}

/// <summary>
/// Stable stages used to locate failures inside diagnostic-system operations.
/// </summary>
public enum DiagnosticFailureStage
{
    PrepareBundle,
    ReadTelemetry,
    ReadLogs,
    ReadSession,
    BuildBundle,
    CommitBundle,
    CreateSession,
    WriteSession,
    ReadSessionMarker,
    RecoverSession,
    EndSession,
    RecordMarker,
    CaptureWindow,
    SaveAttachment
}
