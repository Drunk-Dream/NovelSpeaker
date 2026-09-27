using System.Text;
using Jint;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Scripting;

/// <summary>
/// Evaluates converted templates inside a restricted in-process Jint engine.
/// </summary>
public sealed partial class JintTemplateEvaluator : ITemplateEvaluator
{
    private const int MaxStatements = 256;
    private const int MaxRecursionDepth = 32;
    private const int MaxOutputLength = 8192;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    public Task<string> EvaluateAsync(
        NormalizedTemplate template,
        TtsRuleContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvaluateCore(template, context, cancellationToken));
    }

    private static string EvaluateCore(
        NormalizedTemplate template,
        TtsRuleContext context,
        CancellationToken cancellationToken)
    {
        var engine = CreateEngine(context);
        var builder = new StringBuilder();

        foreach (var segment in template.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (segment)
            {
                case LiteralTemplateSegment literal:
                    builder.Append(literal.Text);
                    break;
                case ExpressionTemplateSegment expression:
                    builder.Append(EvaluateExpression(engine, expression.Expression));
                    break;
            }

            if (builder.Length > MaxOutputLength)
            {
                throw new InvalidOperationException("模板求值结果超过允许的最大长度。");
            }
        }

        return builder.ToString();
    }

    private static Engine CreateEngine(TtsRuleContext context)
    {
        var engine = new Engine(options =>
        {
            options.Strict();
            options.TimeoutInterval(Timeout);
            options.MaxStatements(MaxStatements);
            options.LimitRecursion(MaxRecursionDepth);
        });

        var setupScript =
            $$"""
            const speakText = {{System.Text.Json.JsonSerializer.Serialize(context.SpeakText)}};
            const speakSpeed = {{context.SpeakSpeed}};
            """;

        engine.Execute(setupScript);
        engine.SetValue("btoa", new Func<string, string>(EncodeBase64));
        engine.SetValue("atob", new Func<string, string>(DecodeBase64));
        return engine;
    }

    private static string EncodeBase64(string value)
    {
        if (value.Any(character => character > byte.MaxValue))
        {
            throw new FormatException("btoa 只接受 Latin-1 字符。");
        }

        return Convert.ToBase64String(Encoding.Latin1.GetBytes(value));
    }

    private static string DecodeBase64(string value) =>
        Encoding.Latin1.GetString(Convert.FromBase64String(value));

    private static string EvaluateExpression(Engine engine, string expression)
    {
        var value = engine.Evaluate($"({expression})");
        if (value.IsNull() || value.IsUndefined())
        {
            return string.Empty;
        }

        if (value.IsObject())
        {
            engine.SetValue("__novelSpeakerResult", value);
            var jsonValue = engine.Evaluate("JSON.stringify(__novelSpeakerResult)");
            if (jsonValue.IsNull() || jsonValue.IsUndefined())
            {
                return string.Empty;
            }

            var objectText = jsonValue.ToString();
            return objectText.Length > MaxOutputLength
                ? throw new InvalidOperationException("模板对象结果超过允许的最大长度。")
                : objectText;
        }

        var text = value.ToString();
        return text.Length > MaxOutputLength
            ? throw new InvalidOperationException("模板表达式结果超过允许的最大长度。")
            : text;
    }

}
