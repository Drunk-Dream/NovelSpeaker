using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class TextThemeArchitectureTests
{
    [Fact]
    public void All_product_xaml_text_and_foreground_definitions_use_theme_resources()
    {
        var repository = ArchitectureTestRepository.Locate();
        var violations = TextThemeArchitectureRules.FindViolations(repository.ReadProductXamlFiles());
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Theme_guard_rejects_missing_static_and_literal_foregrounds_and_accepts_style_inheritance()
    {
        var source = new SourceFileDescriptor("theme-contract.xaml", "src/NovelSpeaker.App", """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="TextBase" TargetType="TextBlock">
                    <Setter Property="Foreground" Value="{DynamicResource App.Brush.Text.Primary}" />
                </Style>
                <Style x:Key="Derived" TargetType="TextBlock" BasedOn="{StaticResource TextBase}" />
                <TextBlock Style="{StaticResource Derived}" />
                <TextBlock Foreground="{DynamicResource App.Brush.Text.Secondary}" />
                <TextBlock Foreground="{Binding Foreground}" />
                <TextBlock>
                    <TextBlock.Style>
                        <Style TargetType="TextBlock" BasedOn="{StaticResource Derived}" />
                    </TextBlock.Style>
                </TextBlock>
                <TextBlock />
                <TextBlock Foreground="Black" />
                <TextBlock Foreground="{StaticResource App.Brush.Text.Primary}" />
                <Style x:Key="Invalid" TargetType="TextBlock">
                    <Setter Property="Foreground" Value="#000000" />
                </Style>
                <TextBlock Style="{StaticResource Invalid}" />
                <TextBlock Style="{StaticResource Derived}">
                    <TextBlock.Foreground>
                        <SolidColorBrush Color="Black" />
                    </TextBlock.Foreground>
                </TextBlock>
            </ResourceDictionary>
            """);

        var violations = TextThemeArchitectureRules.FindViolations([source]);
        Assert.Equal(8, violations.Count);
        Assert.Contains(violations, violation => violation.Contains("TextBlock must declare", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("Foreground setter", StringComparison.Ordinal));
    }
}
