namespace NovelSpeaker.Infrastructure.Persistence;

internal sealed record SqliteMigration(int Version, string Sql);
