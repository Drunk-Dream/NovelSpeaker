namespace NovelSpeaker.Application.Speech.Compilation;

/// <summary>Accepts a template failure plus known sensitive values for safe infrastructure diagnostics.</summary>
public interface ITtsCompilationFailureReporter
{
    void Report(string operation, Exception exception, IEnumerable<string?> knownSecrets);
}
