using Microsoft.Extensions.Logging;
using System.Text.Json;
using NovelSpeaker.Application.Speech.Security;

namespace NovelSpeaker.Infrastructure.Speech;

/// <summary>
/// Writes exception diagnostics without passing the original exception object to logging providers.
/// </summary>
internal static class SensitiveFailureLogger
{
    public static void LogError(
        ILogger logger,
        string operation,
        Exception exception,
        IEnumerable<string?> knownSecrets)
    {
        var summary = SensitiveDataRedactor.RedactKnownSecrets(
            exception.Message,
            ExpandKnownSecrets(knownSecrets));
        summary = SensitiveDataRedactor.RedactPlainText(summary ?? string.Empty);

        logger.LogError(
            "{Operation} failed with {ExceptionType}: {ExceptionSummary}",
            operation,
            exception.GetType().Name,
            summary);
    }

    private static IEnumerable<string> ExpandKnownSecrets(IEnumerable<string?> knownSecrets)
    {
        foreach (var secret in knownSecrets.OfType<string>().Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return secret;

            if (secret.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                yield return secret["Bearer ".Length..];
            }

            if (Uri.TryCreate(secret, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query))
            {
                foreach (var value in EnumerateQueryValues(uri.Query[1..]))
                {
                    yield return value;
                }
            }

            foreach (var value in EnumerateJsonStringValues(secret))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> EnumerateQueryValues(string query)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = pair.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex >= 0 && equalsIndex < pair.Length - 1)
            {
                yield return Uri.UnescapeDataString(pair[(equalsIndex + 1)..]);
            }
        }
    }

    private static IEnumerable<string> EnumerateJsonStringValues(string value)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
        }

        if (document is null)
        {
            yield break;
        }

        using (document)
        {
            foreach (var item in EnumerateJsonStringValues(document.RootElement))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<string> EnumerateJsonStringValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }

            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var value in EnumerateJsonStringValues(property.Value))
                {
                    yield return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in EnumerateJsonStringValues(item))
                {
                    yield return value;
                }
            }
        }
    }
}
