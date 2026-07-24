namespace NovelSpeaker.App.Bootstrap;

internal static class StartupFailureProjector
{
    public static StartupFailure Project(StartupStage stage)
    {
        var message = stage switch
        {
            StartupStage.Directories => "无法准备应用数据目录。请检查当前用户的本地存储权限后重试。",
            StartupStage.Settings => "无法读取应用设置。请稍后重试。",
            StartupStage.Logging => "无法建立安全诊断日志。请稍后重试。",
            StartupStage.DependencyInjection => "无法装配应用服务。请重新启动应用。",
            StartupStage.Database => "无法初始化或恢复本地数据库。主窗口尚未打开，请检查日志后重试。",
            StartupStage.Shell => "无法创建主窗口。请重新启动应用。",
            _ => "应用启动失败。请稍后重试。"
        };

        return new StartupFailure(stage, "NovelSpeaker 启动失败", message);
    }
}
