namespace NovelSpeaker.Application.Books;

public sealed class RegexReplacementRulesChangedEventArgs : EventArgs
{
    public RegexReplacementRulesChangedEventArgs(
        RegexReplacementRulesChangeKind kind,
        bool affectsSpeechProfile)
    {
        Kind = kind;
        AffectsSpeechProfile = affectsSpeechProfile;
    }

    public RegexReplacementRulesChangeKind Kind { get; }

    public bool AffectsSpeechProfile { get; }
}

public enum RegexReplacementRulesChangeKind
{
    Saved,
    EnabledChanged,
    Imported,
    Reordered,
    Deleted
}
