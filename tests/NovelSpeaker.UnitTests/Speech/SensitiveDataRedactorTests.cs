using NovelSpeaker.Infrastructure.Speech;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class SensitiveDataRedactorTests
{
    [Theory]
    [InlineData("cookie=session-secret")]
    [InlineData("loginInfo=login-secret")]
    public void RedactPlainText_removes_cookie_and_login_info_values(string value)
    {
        var redacted = SensitiveDataRedactor.RedactPlainText(value);

        Assert.Contains("***", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactJsonLikeText_removes_nested_cookie_and_login_info_values()
    {
        var redacted = SensitiveDataRedactor.RedactJsonLikeText(
            """{"cookie":"session-secret","loginInfo":{"token":"login-secret"}}""");

        Assert.NotNull(redacted);
        Assert.DoesNotContain("session-secret", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("login-secret", redacted, StringComparison.Ordinal);
        Assert.Contains("***", redacted, StringComparison.Ordinal);
    }
}
