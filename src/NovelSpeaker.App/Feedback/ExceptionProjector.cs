using System;
using System.IO;

namespace NovelSpeaker.App.Feedback;

public sealed class ExceptionProjector : IExceptionProjector
{
    public ProjectedUiError Project(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            OperationCanceledException => new ProjectedUiError("操作已取消。", UiMessageSeverity.Information, true),
            InvalidOperationException => new ProjectedUiError(
                "当前操作无法完成，请检查相关设置后重试。",
                UiMessageSeverity.Warning,
                false),
            IOException => new ProjectedUiError("操作失败，请检查本地文件或缓存后重试。", UiMessageSeverity.Error, false),
            _ => new ProjectedUiError("操作失败，请稍后重试。", UiMessageSeverity.Error, false)
        };
    }
}
