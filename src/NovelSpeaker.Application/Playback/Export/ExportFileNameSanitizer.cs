using System.Buffers;

namespace NovelSpeaker.Application.Playback.Export;

/// <summary>
/// Produces one Windows-safe file-name segment. It never accepts or returns a path.
/// </summary>
public sealed class ExportFileNameSanitizer
{
    private const string FallbackName = "未命名";
    private static readonly SearchValues<char> InvalidCharacters =
        SearchValues.Create("<>:\"/\\|?*");
    private static readonly HashSet<string> ReservedBaseNames = CreateReservedBaseNames();

    public string Sanitize(string? value, int maximumLength)
    {
        if (maximumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        var source = value?.Trim() ?? string.Empty;
        if (source is "." or "..")
        {
            source = "_";
        }

        var buffer = new char[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            buffer[index] = char.IsControl(character) || InvalidCharacters.Contains(character)
                ? '_'
                : character;
        }

        var result = new string(buffer);
        result = TrimToLength(result, maximumLength).TrimEnd(' ', '.');
        if (result.Length == 0)
        {
            result = TrimToLength(FallbackName, maximumLength);
        }

        var dotIndex = result.IndexOf('.');
        var baseName = (dotIndex < 0 ? result : result[..dotIndex]).TrimEnd(' ', '.');
        if (ReservedBaseNames.Contains(baseName))
        {
            result = TrimToLength($"_{result}", maximumLength).TrimEnd(' ', '.');
        }

        return result.Length == 0 ? "_" : result;
    }

    private static string TrimToLength(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var length = maximumLength;
        if (length > 0 &&
            length < value.Length &&
            char.IsHighSurrogate(value[length - 1]) &&
            char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        return value[..length];
    }

    private static HashSet<string> CreateReservedBaseNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL"
        };
        for (var index = 1; index <= 9; index++)
        {
            names.Add($"COM{index}");
            names.Add($"LPT{index}");
        }

        return names;
    }
}
