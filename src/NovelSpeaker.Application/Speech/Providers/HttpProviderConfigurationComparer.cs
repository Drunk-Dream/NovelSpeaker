using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Compares portable HTTP configuration without evaluating templates or contacting services.</summary>
public static class HttpProviderConfigurationComparer
{
    public static bool IsSamePortableProvider(
        string leftName,
        HttpSpeechProviderConfiguration left,
        string rightName,
        HttpSpeechProviderConfiguration right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (!string.Equals(leftName.Trim(), rightName.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.UrlTemplate, right.UrlTemplate, StringComparison.Ordinal) ||
            !string.Equals(left.Method.Trim(), right.Method.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.BodyTemplate, right.BodyTemplate, StringComparison.Ordinal) ||
            left.RateLimit != right.RateLimit ||
            left.Headers.Count != right.Headers.Count)
        {
            return false;
        }

        var rightHeaders = new Dictionary<string, string>(right.Headers, StringComparer.OrdinalIgnoreCase);
        return left.Headers.All(header =>
            rightHeaders.TryGetValue(header.Key, out var value) &&
            string.Equals(header.Value, value, StringComparison.Ordinal));
    }
}
