using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal static class TtsRulePersistenceMapper
{
    public static TtsRuleRow FromDomain(HttpTtsRule rule) => new(
        rule.Id,
        rule.Name,
        rule.Url,
        rule.ContentType,
        rule.ConcurrentRate,
        TtsRuleStructuredFieldsCodec.SerializeHeaders(rule.Headers),
        TtsRuleStructuredFieldsCodec.SerializeRequestOptions(rule.RequestMethod, rule.RequestBody, rule.RequestBodyIsJsonStructure),
        rule.LastUpdateTime,
        rule.IsEnabled,
        rule.LastUsedAt is null ? null : SqliteDateTimeMapper.Format(rule.LastUsedAt.Value),
        SqliteDateTimeMapper.Format(rule.CreatedAt),
        SqliteDateTimeMapper.Format(rule.UpdatedAt));

    public static HttpTtsRule ToDomain(TtsRuleRow row) => new(
        row.Id,
        row.Name,
        row.Url,
        row.ContentType,
        row.ConcurrentRate,
        TtsRuleStructuredFieldsCodec.ParseHeaders(row.Header),
        TtsRuleStructuredFieldsCodec.ParseRequestMethod(row.RequestOptionsJson),
        TtsRuleStructuredFieldsCodec.ParseRequestBody(row.RequestOptionsJson),
        TtsRuleStructuredFieldsCodec.IsRequestBodyJsonStructure(row.RequestOptionsJson),
        row.LastUpdateTime,
        row.IsEnabled,
        row.LastUsedAt is null ? null : SqliteDateTimeMapper.Parse(row.LastUsedAt),
        SqliteDateTimeMapper.Parse(row.CreatedAt),
        SqliteDateTimeMapper.Parse(row.UpdatedAt));

}
