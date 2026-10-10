# T002 — Presentation 其余测试合并与层级去重

依赖：T001。规划基线 `main` @ `833333d`，执行时以 T001 已合入的实际 HEAD 为准。

## 目标

将 Presentation 测试进一步从中间状态验证收敛到关键页面/命令行为，使最终分层预算趋近 **125 例**。不修改生产源码，不为了缩减测试而改变产品行为。

## 必读

`AGENTS.md`、`docs/08_QUALITY_AND_TESTING.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/06_UI_AND_VISUAL_SYSTEM.md`、`docs/specs/HTTP_TTS.md`、`docs/specs/BATCH_MANAGEMENT.md`，及本任务涉及的真实 ViewModel/owner。

## 候选范围与删减顺序

1. Rules / Provider 编辑：`ViewModels/SpeechServicesViewModelTests.cs`、`ChapterRulesViewModelTests.cs`、`RegexReplacementRulesViewModelTests.cs`、`MetadataRuleWorkbenchViewModelTests.cs`、`Shared/RuleEditorLifecycleTests.cs`、`Shared/RuleDocumentInteractionTests.cs`、`Shared/WorkbenchInteractionTests.cs`、`Selection/ManagementSelectionControllerTests.cs`。删除共享编辑 owner 在多个 ViewModel 被重复完整测试、每种编辑状态的笛卡尔积、文案/控件/特定调用顺序。**保留** dirty 保存/放弃/取消、Provider 保存与切换、管理模式批量部分失败、导入导出安全、异步旧结果拒绝和错误反馈。
2. 其余 Presentation：`AppRouteNavigationTests.cs`、`GuardedNavigationServiceTests.cs`、`PageActivationControllerTests.cs`、`PageEventOperationRunnerTests.cs`、`LatestOperationSlotTests.cs`、`OwnedTaskRegistryTests.cs`、`Desktop/*Tests.cs`、`Bootstrap/*Tests.cs`、`Diagnostics/*Tests.cs`、`Theming/*Tests.cs`、`Input/*Tests.cs`。优先删除重复对 .NET/WPF 通用功能的证明、单个事件通知次数、资源/绘制形状、容器实现和所有等价取消点的排列。
3. 清理其余不符合准入条件的 Presentation 测试，必要时把多个微断言合为稳定端到端命令测试；检索测试被删除后才失去引用的测试替身。不得靠修改 filter、Skip、`csproj` 排除源文件或 CI job 来缩减数值。

## 留存边界

保留核心导航可达性、Provider 编辑/切换与安全、文件导入/规则编辑、批量部分成功、页面生命期阻断迟到提交、进程 fatal 诊断正确归因以及重要主题文字可读/依赖边界；Presentation 不创建真正 WPF window。界面圆角、具体排序指示线、视觉模板层次等属于任务内临时视觉验证，而非永久测试。

## 验收

- 输出 Presentation/其它项目新的真实 test case 数，目标 Presentation 约 125（建议 110–145）。无法到达时必须说明哪些不可替代风险需要保留。
- Presentation 全部 focused test 通过；WPF 与其它未改测试可以发现和构建，Release build + format/diff 通过；无人使用的辅助类和冗余 fixtures 已清除。
- Backlog 简述删除/合并类别、每一组关键保留风险与执行结果，删除本 task spec。
