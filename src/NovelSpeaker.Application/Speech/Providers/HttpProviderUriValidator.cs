namespace NovelSpeaker.Application.Speech.Providers;

public static class HttpProviderUriValidator
{
    public static bool TryCreateAbsoluteHttpUri(string? value, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            (candidate.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
             candidate.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(candidate.Host))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }
}
