using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public sealed record ProviderConfigurationValidation(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ProviderConfigurationValidation Valid { get; } = new(true, []);
}

public static partial class ProviderConfigurationValidator
{
    public static ProviderConfigurationValidation Validate(SpeechProviderInstance provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!SpeechProviderNameRules.TryNormalize(provider.Name, out var normalizedName))
        {
            return Invalid("Provider 名称不能为空。");
        }

        var configuration = provider.Configuration;
        if (configuration is null)
        {
            return Invalid("Provider 配置不可用。");
        }

        if (configuration.Type == SpeechProviderType.Http && SpeechProviderNameRules.IsReserved(normalizedName))
        {
            return Invalid("Microsoft Edge 是保留名称。");
        }

        return configuration switch
        {
            HttpSpeechProviderConfiguration http => ValidateHttp(http),
            EdgeSpeechProviderConfiguration edge =>
                normalizedName == SpeechProviderNameRules.MicrosoftEdgeName &&
                edge.Voice is { } voice && !string.IsNullOrWhiteSpace(voice.VoiceId) &&
                !string.IsNullOrWhiteSpace(voice.FriendlyName) && !string.IsNullOrWhiteSpace(voice.Locale) &&
                !string.IsNullOrWhiteSpace(voice.Gender)
                    ? ProviderConfigurationValidation.Valid : Invalid("请选择有效的 Voice。"),
            _ => Invalid("Provider 类型或配置不可用。")
        };
    }

    private static ProviderConfigurationValidation ValidateHttp(HttpSpeechProviderConfiguration configuration)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.UrlTemplate))
        {
            errors.Add("HTTP Provider URL 模板不能为空。");
        }
        else if (!IsValidUrlTemplate(configuration.UrlTemplate))
        {
            errors.Add("HTTP Provider URL 模板必须能组成绝对 HTTP 或 HTTPS 地址。");
        }

        if (configuration.Method is not ("GET" or "POST"))
        {
            errors.Add("HTTP Provider 方法仅支持 GET 或 POST。");
        }

        if (configuration.Headers is null)
        {
            errors.Add("HTTP Provider Header 配置不可用。");
            return new ProviderConfigurationValidation(false, errors);
        }

        var headerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in configuration.Headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || !headerNames.Add(header.Key.Trim()) ||
                !IsValidHeaderName(header.Key) ||
                header.Value is null ||
                header.Value.Any(character =>
                    (character < ' ' && character != '\t') || character == '\u007f'))
            {
                errors.Add("HTTP Provider Header 无效。");
                break;
            }

            if (!IsValidTemplate(header.Value))
            {
                errors.Add("HTTP Provider Header 模板格式无效。");
                break;
            }

            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
                !header.Value.Contains("{{", StringComparison.Ordinal) &&
                !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(header.Value, out _))
            {
                errors.Add("HTTP Provider Content-Type 无效。");
                break;
            }
        }

        if (configuration.BodyTemplate is not null && !IsValidTemplate(configuration.BodyTemplate))
        {
            errors.Add("HTTP Provider Body 模板格式无效。");
        }

        if (configuration.Method == "GET" && configuration.BodyTemplate is not null)
        {
            errors.Add("GET 请求不能携带 Body。");
        }

        if (configuration.RateLimit is { MaxRequests: <= 0 } or { WindowMilliseconds: <= 0 })
        {
            errors.Add("HTTP Provider 请求频率限制必须为正数。");
        }

        return errors.Count == 0
            ? ProviderConfigurationValidation.Valid
            : new ProviderConfigurationValidation(false, errors);
    }

    private static bool IsValidTemplate(string text)
    {
        try
        {
            _ = NovelSpeaker.Application.Speech.Compilation.NormalizedTemplate.Parse(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidHeaderName(string name)
    {
        foreach (var character in name)
        {
            if (!(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or
                '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~'))
            {
                return false;
            }
        }

        return name.Length > 0;
    }

    private static bool IsValidUrlTemplate(string text)
    {
        try
        {
            var template = NovelSpeaker.Application.Speech.Compilation.NormalizedTemplate.Parse(text);
            if (!HasAllowedFixedScheme(text))
            {
                return false;
            }

            var hasExpressions = template.Segments.Any(segment =>
                segment is NovelSpeaker.Application.Speech.Compilation.ExpressionTemplateSegment);
            if (hasExpressions &&
                string.IsNullOrWhiteSpace(text[..text.IndexOf("{{", StringComparison.Ordinal)]))
            {
                return true;
            }

            var candidate = string.Concat(template.Segments.Select(segment => segment switch
            {
                NovelSpeaker.Application.Speech.Compilation.LiteralTemplateSegment literal => literal.Text,
                NovelSpeaker.Application.Speech.Compilation.ExpressionTemplateSegment => "placeholder",
                _ => string.Empty
            }));
            return HttpProviderUriValidator.TryCreateAbsoluteHttpUri(candidate, out _);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool HasAllowedFixedScheme(string template)
    {
        var expressionStart = template.IndexOf("{{", StringComparison.Ordinal);
        var fixedPrefix = expressionStart < 0 ? template : template[..expressionStart];
        if (expressionStart >= 0 && string.IsNullOrWhiteSpace(fixedPrefix))
        {
            return true;
        }

        var colon = fixedPrefix.IndexOf(':');
        if (colon < 0)
        {
            return expressionStart < 0;
        }

        var firstPathSeparator = fixedPrefix.IndexOfAny(['/', '?', '#']);
        if (firstPathSeparator >= 0 && firstPathSeparator < colon)
        {
            return false;
        }

        var scheme = fixedPrefix[..colon].Trim();
        return scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderConfigurationValidation Invalid(string error) =>
        new(false, [error]);
}
