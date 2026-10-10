# T001 — 测试发现与 Presentation 核心区域清理

## 目标与边界

以 `main` @ `833333d1e694e8a9c4163e73e0b8face0cacc780` 为规划基线；实际执行使用工作区最新可用 HEAD。完成真实测试用例盘点，并减少 Presentation 中因架构 parser、实现形状、重复生命周期排列带来的低价值测试。**不修改生产源码或长期 `docs/`**。本任务结束后所有测试项目仍可运行。

## 必读

`AGENTS.md`、`docs/08_QUALITY_AND_TESTING.md`、`docs/01_SYSTEM_ARCHITECTURE.md`、`docs/02_RUNTIME_AND_NAVIGATION.md`、`docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`；仅按实际涉及的领域进一步阅读文档。

## 具体实施

1. 在更改前记录 `dotnet test ... --list-tests` / vstest discovery 的每项目**实际可运行用例数**（理论测试按数据展开，排除源码声明/辅助方法数）。与前次 15/293/472/351/102、合计 1233 比较并解释差异。用临时风险表把每组测试标注为“长期核心/高风险、其它层已覆盖、重复、只锁实现细节”；过程产物不入库。
2. 首先复核 `tests/NovelSpeaker.App.PresentationTests/Architecture/`：`ArchitectureTests.cs`、`ArchitectureRuleContractTests.cs`、`ArchitectureRules.cs`、`ApplicationModuleArchitectureRules.cs`、`BehaviorDebtBaselineTests.cs` 等。保留分层依赖、模块环、基础设施越界、重要 state owner、WPF 类型泄漏、安全边界的**有效检查**；优先删除/简化具体目录/文件清单、具体测试项目物理结构、旧版本号、逐字搜索、穷举 parser 检测器自测。避免继续维护自制 C# 小型 parser；确需检测时选最小稳定途径，不新引入一套测试框架。
3. 优先审查 `ViewModels/LibraryViewModelTests.cs`、`BookDetailsViewModelTests.cs`、`CacheManagementViewModelTests.cs`、`ViewModels/Player/PlayerViewModel*Tests.cs`、`Shared/CatalogProjectionTests.cs`、`Shell/ShellActiveCacheControllerTests.cs`、`Shell/ShellChapterExportControllerTests.cs`、`Library/LibraryImportCoordinatorTests.cs`。合并或删除重复的“不同取消点/异常点/状态字段”排列、同一结果的多层复测、精确通知次序、内部方法调用次数、展示文案与 projection 细节。
4. **必须保留代表性核心风险：** 书库导入/删除和新旧页面切换；10k Catalog 不全量重建/不会迟到覆盖；缓存 scope/sparse window 和清理后选择；播放切段切章 UI logical target 先提交、音频准备迟到不会回退或覆盖；阅读位置持久化；页面退出后旧失败不污染新页面；高风险架构依赖与稳定 owner 边界。每项风险优先由一个最合适的测试层验证，不为每一种内部投影路径保留一套。
5. 删除无调用者的 test support、仅服务已删测试的 fixture/parser。只改 `tests/` 下与本任务相关内容；不修改 CI、不设置 Skip、不以弱化断言伪装合并。

## 验收

- 留下“核心风险 → 实际保留测试”的可审核对应证据（可在任务执行过程输出，不需要永久落盘），在 Backlog 成果中简述主要变化。
- Presentation 阶段**建议**压至约 230 例，允许因风险分析上下波动；明确输出真实计数，不可用猜测代替。
- 完成 focused Presentation + Architecture/核心页面测试，`dotnet build -c Release --no-restore`（环境已还原时）、相关 format/diff 检查，确认剩余测试项目仍被发现且无意外跳过。测试文件统一 LF。
- 如果发现真实产品功能缺陷，记录而不顺手修生产行为；不为删除而修改核心逻辑。
