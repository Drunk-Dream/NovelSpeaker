using NovelSpeaker.Application.Playback.Export;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ExportFileNameSanitizerTests
{
    [Theory]
    [InlineData("正常名称", "正常名称")]
    [InlineData("A<B>:C\"D/E\\F|G?H*I", "A_B__C_D_E_F_G_H_I")]
    [InlineData("控制\u0001字符", "控制_字符")]
    [InlineData("尾部.  ", "尾部")]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("CON .txt", "_CON .txt")]
    [InlineData("..", "_")]
    [InlineData("   ", "未命名")]
    public void Sanitize_replaces_windows_unsafe_names(string input, string expected)
    {
        Assert.Equal(expected, new ExportFileNameSanitizer().Sanitize(input, 100));
    }

    [Fact]
    public void Sanitize_honors_length_without_leaving_a_trailing_dot_or_space()
    {
        var result = new ExportFileNameSanitizer().Sanitize("123456789. ", 8);

        Assert.Equal("12345678", result);
    }
}
