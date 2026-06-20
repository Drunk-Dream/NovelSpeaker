using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Imports built-in chapter-detection rules during startup.
/// </summary>
public sealed class DefaultChapterRuleSeeder
{
    private readonly IChapterRuleRepository _repository;

    public DefaultChapterRuleSeeder(IChapterRuleRepository repository)
    {
        _repository = repository;
    }

    public Task SeedAsync(CancellationToken cancellationToken)
    {
        return _repository.ImportDefaultsAsync(cancellationToken);
    }
}
