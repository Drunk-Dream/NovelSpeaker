using System.Globalization;

namespace NovelSpeaker.Infrastructure.Persistence;

internal static class SqliteDateTimeMapper
{
    public static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
