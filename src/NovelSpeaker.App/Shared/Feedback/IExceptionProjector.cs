using System;

namespace NovelSpeaker.App.Shared.Feedback;

public interface IExceptionProjector
{
    ProjectedUiError Project(Exception exception);
}
