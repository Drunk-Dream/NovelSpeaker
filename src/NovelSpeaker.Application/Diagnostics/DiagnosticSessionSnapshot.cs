namespace NovelSpeaker.Application.Diagnostics;

public sealed record DiagnosticSessionSnapshot(
    string SessionId,
    DiagnosticSessionState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long HardCapBytes,
    long RecordedBytes,
    bool CaptureStopped,
    bool EndedUnexpectedly,
    string CurrentProcessInstanceId,
    string? CaptureStoppedReason = null);
