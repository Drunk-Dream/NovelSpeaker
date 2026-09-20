# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **测试体系收敛阶段**。

当前代码基线：`925d53b4966f012c9b1de98fe1223e7d4aefee77`（`dev`，`fix(ui): extend text input hit area across empty space`）。

本轮不新增产品功能。目标是把现有测试从“广泛锁定实现细节”收敛为“少量、稳定、面向核心契约的回归保护”，并让后续 Codex 开发默认遵循新的测试准则：

- 永久测试只保护核心用户流程、数据/兼容性/安全边界和关键架构契约；
- 非核心改动默认不新增永久测试；
- 可以建立临时测试用于复现、研究或验证，但使用完成后必须删除；
- 对核心功能和核心 Bug 推荐 test-first/TDD，但不把 TDD 设为强制流程；
- 测试本身不是产品需求来源，不为保留旧测试而冻结实现。

长期规则见：

- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

本轮生产代码原则上不主动重构。只有删除/合并过度测试后暴露出真正的核心回归，或为了让核心测试能够以稳定行为边界验证而必须进行的最小生产代码调整，才允许修改生产代码。

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短“完成成果”
- `[!]` 阻塞，记录会影响产品/架构/隐私/路线的真实冲突

默认按 T001 → T003 串行执行。如果调用明确要求连续执行整个 Backlog，可以在每个任务自动验收完成后继续，不等待人工验收。

人工验收永远是可选补充，不阻塞任务完成或下一任务。

每个未完成任务的详细实施合同位于 `tasks/`。完成任务后：

1. 满足 task spec 中的自动验收；
2. 删除所有当前任务临时测试、fixture、脚本和诊断产物；
3. 更新本文件状态与“完成成果”；
4. 删除对应 `tasks/Txxx_*.md`；
5. 不等待人工验收。

---

# Phase A：测试资产收敛

## [x] T001（P0）：审计永久测试并建立保留/删除判定

目标：对现有 Domain / Application / Presentation / Infrastructure / WPF 测试按新的永久测试准入标准分类。重点识别锁定实现细节、重复覆盖、低价值 UI/XAML 结构断言和过细 ViewModel/Architecture contract tests，形成可执行的删除、合并、重写清单。

本任务以审计和最小验证为主，不进行大规模生产代码重构。

完成成果：已审计五个测试项目、`tests/TestKit` 与 Quality Matrix。T002 执行下列具名簇；未列入删除/改写簇的测试按 KEEP 保留，其保护的核心契约分别是领域文本规则、Application 的导入/播放/缓存/导出/TTS 安全、Infrastructure 的真实持久化/文件/HTTP/音频边界、Presentation 的核心流程与生命周期、WPF 的可用性与隔离 Desktop。

- Domain：`NarratableTextTests`、`TextSegmentationOptionsTests` KEEP（叙述内容和分段规则）。
- Application：`PlaybackAudioControllerTests`、`PlaybackCommandProcessorTests`、`PlaybackPrefetchCoordinatorTests`、`PlaybackSpeechSegmentComposerTests`、`CacheCatalogTests`、`ExportFileNameSanitizerTests`、`BookFileNameMetadataParserTests` DELETE（低层转发、内部排序或简单映射，风险由播放/缓存/导入/导出流程覆盖）；`PlaybackPositionResolverTests`、`PlaybackRecoveryPolicyTests`、`CacheInvalidationCoordinatorTests`、`AppSettingsServiceTests` MERGE/REWRITE（保留恢复边界、失效范围和设置持久化代表行为，删重复微分支）；其他 KEEP。
- Infrastructure：`Sha256ContentHasherTests`、`SqliteDateTimeMapperTests`、`AppDataDirectoryProviderTests`、`BookDuplicateDetectorTests`、`ChapterRuleManagementServiceTests` DELETE（helper 或被导入/数据库流程覆盖）；`NaudioAudioPlayerTests`、`PlaybackAudioProviderTests`、`SqliteAudioCacheTests`、`PlaybackCoordinatorRecoveryTests`、`TtsRuleUseCaseTests` MERGE/REWRITE（保留可解码播放、缓存一致性、进度恢复、规则导入和安全边界的代表路径）；其他 KEEP。
- Presentation：`CacheCompletenessFormatterTests`、`ShellLayoutControllerTests`、`LibraryScrollStateTests`、`MiniPlayerViewModelTests`、`DiagnosticsAboutViewModelTests`、`ImportTextSettingsViewModelTests`、`PlaybackSettingsViewModelTests`、`AppearanceSettingsViewModelTests`、`AppThemeStartupCoordinatorTests`、`ThemeToggleServiceTests` DELETE（辅助投影、文案/设置 UI 或被核心流程覆盖）；`ArchitectureRuleContractTests` MERGE/REWRITE 为少量代表性规则探针；`BookDetailsViewModelTests`、`PlayerViewModelProjectionTests`、`TtsRulesViewModelTests`、`RegexReplacementRulesViewModelTests` MERGE/REWRITE，保留打开、播放、规则编辑的稳定行为，删投影字段/调用时序断言；其他 KEEP。
- WPF：`Ui/*Style*Tests`、`Ui/*Palette*Tests`、`Ui/*Gallery*Tests`、`Ui/*ResponsiveLayoutTests`、`Ui/*FormControlTests`、`Ui/*ResourceTests`、`Ui/*VisualTests`、`Ui/*FeedbackControlTests`、`Ui/*CallerAuditTests`、`Ui/*ContractTests` DELETE（样式、资源、几何、截图、结构）；`Ui/AppearanceSettingsPageTests`、`Ui/BookCardViewTests`、`Ui/CacheAndDataPageTests`、`Ui/CachePagesViewTests`、`Ui/DiagnosticsAboutPageTests`、`Ui/GeneralSettingsPageTests`、`Ui/ImportTextSettingsPageTests`、`Ui/PlaybackSettingsPageTests`、`Ui/RuleListItemViewTests`、`Ui/RuleDragInteractionTests`、`Ui/TtsRulesPageTests`、`Ui/RegexReplacementRulesPageTests` DELETE（页面布局和控件内部形状）；`Ui/PlayerViewAutoCenterTests` MERGE/REWRITE 为恢复当前位置、手动浏览两项核心交互；`Ui/PlayerViewLayoutTests` MERGE/REWRITE，保留空章节/无可用规则时播放控件状态及 Single Surface 断言；`Ui/SettingsPageViewTests` MERGE/REWRITE，保留键盘导航路线激活；`Ui/LibraryPageTests`、`Ui/BookDetailsPageTests`、`Ui/PlayerStopTimerEntryTests`、`Navigation/MainWindowNavigationTests`、`Desktop/MiniPlayerWindowTests`、`Bootstrap/StartupStatusWindowTests` MERGE/REWRITE：删除截图、样式、精确几何断言，保留关键 binding、虚拟化、导航和窗口生命周期。`Architecture/VisualResourceGraphTests`、`Architecture/IconForegroundContractTests`、`Architecture/VisualStyleArchitectureTests` DELETE（资源图、图标和样式检测器细节）；其他 KEEP，其中 `Architecture/WpfTestHostIsolationTests` 守护隔离桌面失败关闭。
- TestKit：`PageVisualReviewHarness`、`WindowVisualReviewHarness`、`VisualArtifactTestGuard`、`TransientPopupVisualRenderer`、`VisualTreeTestHelper` 是 TEMPORARY-ONLY PATTERN 候选；T003 按删除后的真实引用清理。CI 五项目入口目前有效。未发现必须新增长期测试才能开展瘦身的核心缺口；临时的视觉/内部状态验证须随任务删除。

## [x] T002（P0）：按核心契约瘦身现有测试体系

依赖：T001。

目标：执行 T001 的审计结论，删除/合并/重写不符合长期准入标准的测试。优先收敛 WPF 与 Presentation 细粒度测试，再处理重复的 Application/Infrastructure 测试和过度的 Architecture rule contract tests。

不得按目标测试数量机械删除测试；每个保留测试都应能说明其保护的核心能力或高风险边界。不得为了让旧测试继续通过而恢复旧实现。

完成成果：按 T001 分类删除 WPF 样式、资源、截图、精确几何与页面结构测试；混合测试保留关键 binding、键盘导航、虚拟化、播放器可用状态和四个浮层的 Single Surface 运行态检查。Presentation 删除辅助投影/设置微测试，将 Architecture 检测器探针压缩为代表性边界，并保留书籍详情打开章节、播放失败重试等独立审查确认的核心路径。Application/Infrastructure 删除简单转发、映射和已由高层流程覆盖的测试；合并播放恢复、缓存持久化与失效、设置保存、音频资源释放、TTS 规则导入等重复分支，保留各自的数据、兼容与安全边界。删除失去引用的 Visual Review TestKit 与截图辅助设施。生产代码未改动；后续 T003 检查 CI、剩余辅助资产及完整门禁。

## [ ] T003（P0）：收口质量门禁与后续测试工作流

依赖：T002。

实施规格：`tasks/T003_quality_gate_closure.md`

目标：基于瘦身后的测试资产检查 CI/质量门禁是否仍与新的长期测试策略一致，删除只服务于已移除细粒度测试的 TestKit/fixture/辅助代码，并执行完整 Release 质量门禁。

若现有 `.github/workflows/quality-matrix.yml` 在测试项目结构不变的情况下仍然合适，不为“看起来更简单”而修改 CI。只有存在实际重复、失效入口或已无任何长期测试的项目时才调整。

完成后应保证：

- 全量剩余永久测试均保护核心契约或明确高风险边界；
- 非核心改动没有“必须配永久测试”的默认要求；
- 临时测试的创建与删除规则已经可以被 Codex 实际遵循；
- 后续核心功能/核心 Bug 可以优先采用 test-first，但 Agent 可以根据风险选择更合适的验证方式；
- 完整 Release build 与全部保留测试通过。
