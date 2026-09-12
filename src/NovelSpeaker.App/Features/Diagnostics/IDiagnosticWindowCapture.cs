using NovelSpeaker.Application.Diagnostics;

namespace NovelSpeaker.App.Features.Diagnostics;

public interface IDiagnosticWindowCapture
{
    Task<DiagnosticAttachment> CaptureCurrentWindowAsync(CancellationToken cancellationToken);
}
