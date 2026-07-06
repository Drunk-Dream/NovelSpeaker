using System.Text;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.Text;

/// <summary>
/// Detects BOM, strict UTF-8, and GB18030 for TXT files.
/// </summary>
public sealed class TextFileAnalyzer : ITextFileAnalyzer
{
    private const int PreviewLength = 800;
    private const int ConfidenceSampleLength = 4096;

    public TextFileAnalyzer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<TextFileAnalysis> AnalyzeAsync(
        BookImportRequest request,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(CreateProgress(BookImportPhase.DetectingEncoding, 0, request.FilePath, "正在识别文本编码。"));

        if (!string.IsNullOrWhiteSpace(request.EncodingOverride))
        {
            var specified = GetEncoding(request.EncodingOverride);
            var specifiedText = await ReadAllTextAsync(request.FilePath, specified, progress, cancellationToken);
            return CreateResult(
                NormalizeEncodingName(request.EncodingOverride),
                specifiedText,
                TextEncodingDetectionMode.ManualOverride);
        }

        var fileEncoding = await DetectEncodingAsync(request.FilePath, cancellationToken);

        try
        {
            var utf8Text = await ReadAllTextAsync(request.FilePath, fileEncoding.Encoding, progress, cancellationToken);
            return CreateResult(
                NormalizeEncodingName(fileEncoding.Encoding.WebName),
                utf8Text,
                fileEncoding.Mode);
        }
        catch (DecoderFallbackException)
        {
            var gb18030 = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var gbText = await ReadAllTextAsync(request.FilePath, gb18030, progress, cancellationToken);
            return CreateResult("gb18030", gbText, TextEncodingDetectionMode.Gb18030Fallback);
        }
    }

    private static async Task<DetectedEncoding> DetectEncodingAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var bom = new byte[3];
        var read = await stream.ReadAsync(bom.AsMemory(0, bom.Length), cancellationToken);

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return new DetectedEncoding(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
                TextEncodingDetectionMode.BomUtf8);
        }

        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
        {
            return new DetectedEncoding(
                new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true),
                TextEncodingDetectionMode.BomUtf16Le);
        }

        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
        {
            return new DetectedEncoding(
                new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true),
                TextEncodingDetectionMode.BomUtf16Be);
        }

        return new DetectedEncoding(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            TextEncodingDetectionMode.StrictUtf8);
    }

    private static async Task<string> ReadAllTextAsync(
        string filePath,
        Encoding encoding,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[4096];
        var builder = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
            progress?.Report(new BookImportProgress(
                BookImportPhase.DetectingEncoding,
                stream.Position,
                fileInfo.Length,
                fileInfo.Length == 0,
                "正在读取小说内容。"));
        }

        return builder.ToString();
    }

    private static TextFileAnalysis CreateResult(
        string encodingName,
        string text,
        TextEncodingDetectionMode detectionMode)
    {
        var (isLowConfidence, lowConfidenceReason) = EvaluateConfidence(text, detectionMode);
        return new TextFileAnalysis(
            encodingName,
            text[..Math.Min(text.Length, PreviewLength)],
            text,
            detectionMode,
            isLowConfidence,
            lowConfidenceReason);
    }

    private static BookImportProgress CreateProgress(BookImportPhase phase, long bytesProcessed, string filePath, string message)
    {
        var totalBytes = new FileInfo(filePath).Length;
        return new BookImportProgress(phase, bytesProcessed, totalBytes, totalBytes == 0, message);
    }

    private static string NormalizeEncodingName(string encodingName) =>
        encodingName.ToLowerInvariant() switch
        {
            "utf-16" => "utf-16le",
            "unicode" => "utf-16le",
            "utf-16be" => "utf-16be",
            _ => encodingName.ToLowerInvariant()
        };

    private static Encoding GetEncoding(string encodingName) =>
        encodingName.ToLowerInvariant() switch
        {
            "utf-8" => new UTF8Encoding(false, true),
            "utf-16le" => new UnicodeEncoding(false, false, true),
            "utf-16be" => new UnicodeEncoding(true, false, true),
            "gb18030" => Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            _ => throw new NotSupportedException($"Unsupported encoding: {encodingName}")
        };

    private static (bool IsLowConfidence, LowConfidenceReason? Reason) EvaluateConfidence(
        string text,
        TextEncodingDetectionMode detectionMode)
    {
        if (detectionMode == TextEncodingDetectionMode.Gb18030Fallback)
        {
            return (true, LowConfidenceReason.FallbackEncoding);
        }

        if (detectionMode != TextEncodingDetectionMode.StrictUtf8)
        {
            return (false, null);
        }

        var sampleLength = Math.Min(ConfidenceSampleLength, text.Length);
        if (sampleLength == 0)
        {
            return (false, null);
        }

        var suspiciousCount = 0;
        for (var index = 0; index < sampleLength; index++)
        {
            var current = text[index];
            if (current == '\0' ||
                current == '\uFFFD' ||
                IsPrivateUse(current) ||
                IsSuspiciousControl(current))
            {
                suspiciousCount++;
            }
        }

        return suspiciousCount >= 3 && suspiciousCount / (double)sampleLength > 0.02d
            ? (true, LowConfidenceReason.SuspiciousCharacters)
            : (false, null);
    }

    private static bool IsPrivateUse(char value)
    {
        return value is >= '\uE000' and <= '\uF8FF';
    }

    private static bool IsSuspiciousControl(char value)
    {
        return char.IsControl(value) && value is not '\r' and not '\n' and not '\t';
    }

    private sealed record DetectedEncoding(Encoding Encoding, TextEncodingDetectionMode Mode);
}
