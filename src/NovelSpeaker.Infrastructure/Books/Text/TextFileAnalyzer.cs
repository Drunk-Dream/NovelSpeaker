using System.Text;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.Text;

/// <summary>
/// Detects BOM, strict UTF-8, and GB18030 for TXT files.
/// </summary>
public sealed class TextFileAnalyzer : ITextFileAnalyzer
{
    public TextFileAnalyzer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<TextFileAnalysis> AnalyzeAsync(
        string filePath,
        string? encodingName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(encodingName))
        {
            var specified = GetEncoding(encodingName);
            var specifiedText = await File.ReadAllTextAsync(filePath, specified, cancellationToken);
            return new TextFileAnalysis(
                encodingName.ToLowerInvariant(),
                specifiedText[..Math.Min(specifiedText.Length, 800)],
                specifiedText);
        }

        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var utf8Text = await File.ReadAllTextAsync(filePath, utf8, cancellationToken);
            return new TextFileAnalysis(
                "utf-8",
                utf8Text[..Math.Min(utf8Text.Length, 800)],
                utf8Text);
        }
        catch (DecoderFallbackException)
        {
            var gb18030 = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var gbText = await File.ReadAllTextAsync(filePath, gb18030, cancellationToken);
            return new TextFileAnalysis(
                "gb18030",
                gbText[..Math.Min(gbText.Length, 800)],
                gbText);
        }
    }

    private static Encoding GetEncoding(string encodingName) =>
        encodingName.ToLowerInvariant() switch
        {
            "utf-8" => new UTF8Encoding(false, true),
            "gb18030" => Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            _ => throw new NotSupportedException($"Unsupported encoding: {encodingName}")
        };
}
