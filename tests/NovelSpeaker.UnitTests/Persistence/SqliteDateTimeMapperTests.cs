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

            Assert.Equal("2026-07-16T09:08:07.6543210+05:30", encoded);
            Assert.Equal(value, decoded);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
