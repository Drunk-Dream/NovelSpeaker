using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.Application.Diagnostics;

/// <summary>
/// Application port for the explicit diagnostic-session owner.
/// No persistence or platform type is exposed here.
/// </summary>
public interface IDiagnosticSessionService
{
    DiagnosticSessionSnapshot? Current { get; }

    DiagnosticSessionSnapshot? LastEnded { get; }

    Task<DiagnosticSessionSnapshot> StartAsync(
        DiagnosticSessionStartOptions options,
        CancellationToken cancellationToken);

    Task<DiagnosticSessionSnapshot?> RecoverAsync(CancellationToken cancellationToken);

    Task<DiagnosticSessionSnapshot> EndAsync(CancellationToken cancellationToken);

    Task NotifyProcessShutdownAsync(CancellationToken cancellationToken);

    Task AddAttachmentAsync(DiagnosticAttachment attachment, CancellationToken cancellationToken);

    Task<bool> RecordProblemMarkerAsync(CancellationToken cancellationToken);

    string GetOrCreateAnonymousObjectToken(string objectType, string objectIdentity);

    void Record(DiagnosticDefinition definition, DiagnosticFieldSet fields);
}
