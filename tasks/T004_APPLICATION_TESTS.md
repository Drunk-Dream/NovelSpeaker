# T004 — Application 层核心业务测试去重

依赖：T003。规划基线 `main` @ `833333d`，执行以当前 HEAD 为准。

## 目标

将 `tests/NovelSpeaker.Application.UnitTests/` 的历史 293 例收敛至约 **180 例**；`Domain.UnitTests` 的 15 例原则上全部保留。测试应表达稳定业务规则、关键 use case 和跨 async 边界的 observable outcome，不保护任意重构可改变的内部状态排列。

## 必读

`AGENTS.md`、`docs/08_QUALITY_AND_TESTING.md`、`docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`、`docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`；涉及 Provider 时阅读 `docs/specs/HTTP_TTS.md`。

## 分组候选

1. Playback：`PlaybackRuntimeTests.cs`、`PlaybackSegmentRunnerTests.cs`、`PlaybackRecoveryPolicyTests.cs`、`PlaybackPositionResolverTests.cs`、`PlaybackPrefetchCoordinatorTests.cs`、`PlaybackProgressControllerTests.cs`、`PlaybackStopTimerTests.cs`、`LocalAudioPlaybackCoordinatorTests.cs`。同一“旧音频/旧目标被拒绝”“事务前失败不提交”“stop/pause/checkpoint 语义”风险只保留几组能区分失败模型的代表性测试；删除细粒度 field-copy、资源释放次序、无影响的 reducer 状态排列。
2. Books/文本/Export：`Books/BookImportServiceTests.cs`、`Books/ChapterSplitterTests.cs`、`Books/RegexReplacementPipelineTests.cs`、`Books/*WorkspaceServiceTests.cs`、`ExportChaptersServiceTests.cs`、`ChapterExportCoordinatorTests.cs`。合并等价输入和重复低价值分支；不要删除原子提交、失败清理、文件/编码重要边界、规则沙箱或导出完整性。
3. Provider/Cache/Observability：`ProviderFoundationTests.cs`、`HttpProviderWorkflowTests.cs`、`EdgeProviderTests.cs`、`TtsExecutionServiceTests.cs`、`ActiveCacheCoordinatorTests.cs`、`CacheReadModelTests.cs`、`ObservabilityContractTests.cs`、`Settings/AppSettingsServiceTests.cs`、`Desktop/MediaControlCoordinatorTests.cs`。聚焦稳定状态转换、一次代表性错误/取消、Provider 配置安全、诊断失败不污染业务；移除相同风险不同 caller 层多次测试和不影响用户的中间通知计数。

## 不能删除的语义

- Book 导入/提交后的身份、已提交变更、失败回滚与取消；外部 TXT 不修改。
- Playback 的切段/切章 logical target 先提交、旧 target 与旧 preparation 音频严格拒绝、停止/暂停/恢复、当前位置保存/恢复、切 Provider 不打断当前句且下一句采用新配置。
- Active Cache 的 Provider/config 冻结、去重和缓存身份失效；正式 Export 的文件/音频边界；HTTP/Edge 的配置解析、模板 sandbox、Header 安全和重要失败处理。
- 可观察的安全/隐私契约与失败隔离、进程资源与 Operation 监测独立、诊断会话核心状态。

## 执行与验收

- 每个删除/合并组能说明已由保留的哪个高层行为测试覆盖或为什么仅属内部实现；绝不合并为“只断言不抛异常”的无效测试。
- 建议 Application 165–195 例，Domain 保留 15（若出现唯一风险之外的重复，先说明而非机械删除）。统计 `[Theory]` 展开数；运行两个 UnitTests 项目及相关 Infrastructure 代表性端到端 focused test，Release build/format/diff 通过。
- 清理最后使用方消失的 Fake/test-only helper；不调整生产 API 以适配已删的测试。
