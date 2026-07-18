using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Infrastructure.Speech;

internal static class TtsRequestKnownSecrets
{
    public static IEnumerable<string?> Enumerate(ParsedTtsRequest request)
    {
        yield return request.Url.ToString();
        yield return request.Body.RawText;

        foreach (var header in request.Headers)
        {
            yield return header.Key;
            yield return header.Value;
        }

        if (request.Body.FormFields is null)
        {
            yield break;
        }

        foreach (var field in request.Body.FormFields)
        {
            yield return field.Key;
            yield return field.Value;
        }
    }
}
