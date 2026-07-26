using System.Globalization;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Persistence;

public sealed class SqliteDateTimeMapperTests
{
    [Fact]
    public void RoundTrip_is_invariant_under_non_default_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
            var value = new DateTimeOffset(2026, 7, 16, 9, 8, 7, TimeSpan.FromHours(5.5)).AddTicks(6543210);

            var encoded = SqliteDateTimeMapper.Format(value);
            var decoded = SqliteDateTimeMapper.Parse(encoded);

            Assert.Equal("2026-07-16T03:38:07.6543210+00:00", encoded);
            Assert.Equal(value, decoded);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData("2026-07-16 09:08:07", "2026-07-16T09:08:07.0000000+00:00")]
    [InlineData("2026-07-16T09:08:07Z", "2026-07-16T09:08:07.0000000+00:00")]
    [InlineData("2026-07-16T09:08:07.1234567+08:00", "2026-07-16T09:08:07.1234567+08:00")]
    public void TryParse_accepts_supported_legacy_and_roundtrip_formats(string storedValue, string expected)
    {
        var parsed = SqliteDateTimeMapper.TryParse(storedValue, out var value);

        Assert.True(parsed);
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026-99-99 25:61:61")]
    public void TryParse_rejects_damaged_values_without_throwing(string storedValue)
    {
        Assert.False(SqliteDateTimeMapper.TryParse(storedValue, out _));
    }
}
