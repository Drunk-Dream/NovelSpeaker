# T003 — WPF 测试仅保留不可替代的核心行为

依赖：T002。规划基线 `main` @ `833333d`，具体按前序任务完成后的工作区执行。

## 目标

将 `tests/NovelSpeaker.App.WpfTests/` 从历史 102 例收敛至约 **40 例**。明确判断“必须实例化 WPF 才能发现的产品级风险”；纯 ViewModel/Controller 逻辑在 Presentation 已覆盖时，删除重复 WPF 验证。允许删除整组视觉测试文件及其仅测试使用的构造辅助。

## 必读

`AGENTS.md`、`docs/08_QUALITY_AND_TESTING.md`（尤其 §7 WPF 与 §8 Desktop 隔离）、`docs/06_UI_AND_VISUAL_SYSTEM.md`，以及被修改测试所涉及的 WPF 代码。

## 第一批低价值候选（逐个复核，非直接删除白名单）

- `Ui/PlayerViewAnimationTests.cs`、`PlayerViewLayoutTests.cs`、`PlayerViewAutoCenterTests.cs`、`PlayerViewChapterNavigationScrollTests.cs`、`PlayerViewTestSupport.cs`：具体动画轨迹、当前值/像素居中、视觉模板层次等；保留确实影响切段可用性、焦点/虚拟化且其它层无法发现的代表性场景。
- `Ui/WpfUiFlyoutPlacementTests.cs`、`MouseWheelScrollBehaviorTests.cs`、`NavigationViewportHeightBehaviorTests.cs`、`CurrentItemLocatorInteractionTests.cs`：去掉容器定位/像素组合/事件细节的过度排列；如逻辑行与像素滚动单位错误会使关键虚拟化列表不可用，必须保留一个能发现此回归的实测路径。
- `Ui/LibraryPageTests.cs`、`BookDetailsPageTests.cs`、`ChapterRulesPageTests.cs`、`SettingsPageViewTests.cs`、`CacheManagementPageLifecycleTests.cs`、`Ui/PlayerMiniPlayerEntryTests.cs`、`Ui/PlayerStopTimerEntryTests.cs`、`Ui/DiagnosticToolWindowTests.cs`：收敛为关键页面可创建、核心操作可达、必要生命周期和绑定完整的代表场景；不重复 Presentation 的字段和通知断言。
- `Navigation/MainWindowNavigationTests.cs`、`NavigationPageLifecycleTests.cs`、`RulePageNavigationGuardTests.cs`、`ThemeToggleNavigationContractTests.cs`、`DependencyInjection/ServiceCollectionExtensionsTests.cs`：将不同路径相同语义的组合断言合并，保留核心导航与 DI composition 的真实实例化保护。

## 绝对不能为凑数削弱

1. `Architecture/WpfTestHostIsolationTests.cs` 的隔离 Desktop 建立、释放、错误回退 **fail closed**；真实窗口/Popup/Focus/HWND 不得回落到用户当前桌面，不设 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
2. 一个真实 Provider 或其它曾出现 DataTemplate/StaticResource 延迟实例化崩溃的关键 Popup，可使用最小真实 ViewModel 数据验证打开/布局/渲染而不抛异常；不要断言 Visual Tree 或具体资源 Key。
3. 核心页面/窗口创建、关键 navigation composition、真正会导致用户不可操作的焦点/命中/虚拟化，以及生产启动/托盘关键生命周期。
4. 不能为了让 CI 绿色而禁用 WPF 测试项目、套件或隔离能力；任何剩余真实窗口测试须继续使用 TestKit 的隔离 host。

## 验收

- WPF 发现并运行的用例建议 35–50；记录真实 count 与有依据的保留项。无法低于 50 时如实列明不可替代高风险用例。
- 与被删测试关联的 WPF TestKit fixture 清理后，仍无可见窗口 fallback、无残留独立窗口创建；WPF focused tests、Presentation 核心 focused tests、Release build 与格式检查通过。
- 此任务不改产品 UI，不新增临时视觉副产物到 Git，清理旧辅助文件并简记结果至 Backlog。
