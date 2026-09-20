# T004 — 修正问题诊断会话选择器的目录状态

## 目标

设置页“导出问题诊断”当前连续打开两个 Windows 文件对话框：

1. OpenFileDialog：选择要导出的 `.nsdiag`；
2. SaveFileDialog：选择最终 ZIP 保存位置。

两者的目录语义不同。选择 `.nsdiag` 时应每次直接进入 NovelSpeaker 的 `Diagnostics` 目录；保存 ZIP 时不应被前一个诊断选择器带到 `Diagnostics`，而应继续使用 Windows 对普通保存位置的既有记忆。

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md` 中 T004
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md` 第 8 节
- `docs/08_QUALITY_AND_TESTING.md`
- `PresentationFileDialogOptions`
- `IPresentationFileDialogService`
- `WpfPresentationFileDialogService`
- `DiagnosticsAboutViewModel` 的问题诊断导出流程

以 `dev` 当前源码为实现基线，不恢复已删除的旧 DiagnosticsAboutViewModel/WPF 细粒度测试体系。

## 必须实现的行为

### 1. `.nsdiag` OpenFileDialog

从 Settings 导出旧问题诊断时：

- 每次打开都直接显示当前 `DiagnosticsDirectoryPath`；
- Filter 仍限制为 `.nsdiag`；
- 用户仍可以主动浏览到其他目录；
- 为该“诊断会话选择器”使用固定、独立的 file-dialog persisted-state profile，避免它的浏览行为污染应用其他打开/保存对话框的 Windows 状态。

### 2. ZIP SaveFileDialog

选择完 `.nsdiag` 后打开保存对话框：

- 不设置 `DiagnosticsDirectoryPath` 为初始目录；
- 不复用诊断会话 OpenFileDialog 的 persisted-state profile；
- 继续使用 Windows 对普通保存对话框的已有目录记忆；
- 保持现有本地时间戳 ZIP 默认文件名和导出流程。

刚结束 Session、源 `.nsdiag` 已知的“立即导出”流程仍只需要 SaveFileDialog，不增加额外选择步骤。

## 实现边界

优先扩展现有通用 Presentation file-dialog abstraction，例如让 `PresentationFileDialogOptions` 支持可选：

- `InitialDirectory`
- `ClientGuid` / 等价的 persisted-state profile

`WpfPresentationFileDialogService` 负责把这些平台无关选项映射到 `Microsoft.Win32` Common Item Dialog 能力。

要求：

- 不在 `DiagnosticsAboutViewModel` 中直接 `new OpenFileDialog` / `SaveFileDialog`；
- 不创建 Diagnostics 专用第二套文件对话框服务；
- 不用进程 current directory 等全局副作用模拟初始目录；
- 不自行实现 Shell dialog；当前 .NET/WPF Common Item Dialog 已有的 `InitialDirectory` / `ClientGuid` 能力足够时直接复用；
- Diagnostics 目录来源复用现有 snapshot/property，不在 ViewModel 重算 Data 根路径；
- 如果 framework 的 persisted state 与 initial directory 行为存在差异，以“每次 `.nsdiag` 选择都从 Diagnostics 开始、保存对话框不受污染”的实际产品行为为准。

固定 Client GUID 应集中定义并带有用途名称，不在多个调用点散落 magic GUID。

## 测试与验收

这是明确交互修复，但不属于需要恢复大量 WPF/ViewModel 细粒度永久测试的核心流程。

优先：

1. 运行受影响项目的现有 focused tests；
2. 使用可控 fake / 临时测试验证传给 dialog service 的两个 options 具有不同目录/profile 语义；
3. 如必须验证 Microsoft.Win32 映射，可建立任务内临时测试，完成后删除；
4. 不新增截图、Visual Tree、精确窗口布局测试；
5. 不重新建立上一阶段已删除的 `DiagnosticsAboutViewModelTests`，除非发现一个符合 `docs/08` 永久测试准入标准的更高层核心合同。

至少执行：

```powershell
dotnet format --verify-no-changes
dotnet build -c Release
```

以及受影响的 focused tests。人工点击验证可选，不阻塞任务完成。

## 完成

- 更新 `TASK_BACKLOG.md` 中 T004 为 `[x]` 并记录简短成果；
- 删除本任务规格；
- 删除所有临时测试/脚本；
- 按调用要求继续 T005，不等待人工验收。
