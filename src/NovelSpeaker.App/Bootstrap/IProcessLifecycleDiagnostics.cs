namespace NovelSpeaker.App.Bootstrap;

internal interface IProcessLifecycleDiagnostics
{
    void RecordStage(string name, string safeMessage);

    void RecordFailure(string name, string safeMessage, Exception exception);
}
