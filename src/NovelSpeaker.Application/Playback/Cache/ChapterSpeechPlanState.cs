namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>Persistence state of the current chapter speech plan.</summary>
public enum ChapterSpeechPlanState
{
    Ready = 1,
    Computing = 2,
    Failed = 3
}
