namespace NovelSpeaker.Domain.Speech.Providers;

public static class SpeechProviderNameRules
{
    public const string MicrosoftEdgeName = "Microsoft Edge";

    public static bool TryNormalize(string? name, out string normalizedName)
    {
        normalizedName = name?.Trim() ?? string.Empty;
        return normalizedName.Length > 0;
    }

    public static bool IsReserved(string name) =>
        string.Equals(name.Trim(), MicrosoftEdgeName, StringComparison.OrdinalIgnoreCase);
}
