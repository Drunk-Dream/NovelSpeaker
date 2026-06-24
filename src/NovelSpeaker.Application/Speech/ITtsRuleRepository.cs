using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Owns persistence of imported HTTP TTS rules and their metadata.
/// </summary>
public interface ITtsRuleRepository
{
    Task<IReadOnlyList<HttpTtsRule>> GetAllAsync(CancellationToken cancellationToken);

    Task<HttpTtsRule?> GetByIdAsync(long ruleId, CancellationToken cancellationToken);

    Task<long> SaveAsync(HttpTtsRule rule, CancellationToken cancellationToken);

    Task DeleteAsync(long ruleId, CancellationToken cancellationToken);
}
