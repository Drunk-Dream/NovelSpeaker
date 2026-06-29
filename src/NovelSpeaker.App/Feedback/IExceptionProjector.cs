using System;

namespace NovelSpeaker.App.Feedback;

public interface IExceptionProjector
{
    ProjectedUiError Project(Exception exception);
}
