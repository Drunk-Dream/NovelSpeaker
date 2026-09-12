namespace NovelSpeaker.Application.Diagnostics;

/// <summary>
/// User-provided diagnostic attachment bytes. The first product use is an explicit window capture.
/// </summary>
public sealed record DiagnosticAttachment(
    string AttachmentId,
    DateTimeOffset CapturedAtUtc,
    string MimeType,
    int PixelWidth,
    int PixelHeight,
    byte[] Content)
{
    public long ByteLength => Content.LongLength;
}
