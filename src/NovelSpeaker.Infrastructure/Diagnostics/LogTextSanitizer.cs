using System.Text.RegularExpressions;
using NovelSpeaker.Application.Speech.Security;

namespace NovelSpeaker.Infrastructure.Diagnostics;

internal static partial class LogTextSanitizer
{
    public static string Sanitize(string value, IEnumerable<string>? knownSecrets = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        var redacted = knownSecrets is null
            ? value
            : SensitiveDataRedactor.RedactKnownSecrets(value, knownSecrets) ?? string.Empty;
        redacted = SensitiveDataRedactor.RedactPlainText(redacted);
        redacted = UrlPattern().Replace(redacted, "<url>");
        redacted = WindowsPathPattern().Replace(redacted, "<path>");
        redacted = UnixPathPattern().Replace(redacted, "<path>");
        return redacted.Length <= 2048 ? redacted : redacted[..2048];
    }

    [GeneratedRegex("(?i)https?://[^\\s\\\")]+")]
    private static partial Regex UrlPattern();

    [GeneratedRegex("(?i)(?:[a-z]:\\\\|\\\\\\\\)[^\\r\\n\\\"]+")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?<![a-zA-Z0-9])/(?:[^\r\n""']+/)+[^\r\n""']+")]
    private static partial Regex UnixPathPattern();
}
