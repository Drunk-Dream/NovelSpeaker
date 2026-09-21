# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **Observability 使用体验与性能遥测可分析性优化阶段**。

当前代码基线：`8994f16d73816ec65c0f105bb9369496552440b2`（`dev`，`fix(ui): prevent compact navigation item clipping`）。

上一阶段已经完成测试体系收敛。本轮只处理两个已经确认的问题，不扩张到新的诊断产品范围：

1. 设置页导出已有问题诊断会话时，第一个 `.nsdiag` 选择器应直接进入 `Diagnostics`，且不能污染随后 ZIP 保存对话框的 Windows 目录记忆；
2. 普通性能遥测当前虽然能够做宏观汇总，但缺少连续时间结构、进程边界和稳定功能上下文，CPU/内存又绑定到 Operation 完成时采样，不利于后续长期性能分析。

本轮目标：

- 修正问题诊断导出的两阶段文件对话框目录语义；
- 将进程 CPU/内存改为遥测开启期间约 60 秒一次的独立低频采样；
- 保留约 1 分钟时间窗口与 process instance，使空闲和长时间运行趋势可分析；
- 将单个 `telemetry.json` 扩展为 definitions + aggregates + windows 的自解释交换格式；
- 审计现有 operation vocabulary，使导出的性能事实能够回答“什么时候、哪个稳定功能边界发生了变化”，而不是增加高频原始事件或复杂 profiler。

长期规则见：

- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短“完成成果”
- `[!]` 阻塞，记录会影响产品/架构/隐私/路线的真实冲突

默认按 T004 → T005 串行执行。如果调用明确要求连续执行整个 Backlog，可以在每个任务自动验收完成后继续，不等待人工验收。

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

## [x] T003（P0）：收口质量门禁与后续测试工作流

依赖：T002。

目标：基于瘦身后的测试资产检查 CI/质量门禁是否仍与新的长期测试策略一致，删除只服务于已移除细粒度测试的 TestKit/fixture/辅助代码，并执行完整 Release 质量门禁。

若现有 `.github/workflows/quality-matrix.yml` 在测试项目结构不变的情况下仍然合适，不为“看起来更简单”而修改 CI。只有存在实际重复、失效入口或已无任何长期测试的项目时才调整。

完成后应保证：

- 全量剩余永久测试均保护核心契约或明确高风险边界；
- 非核心改动没有“必须配永久测试”的默认要求；
- 临时测试的创建与删除规则已经可以被 Codex 实际遵循；
- 后续核心功能/核心 Bug 可以优先采用 test-first，但 Agent 可以根据风险选择更合适的验证方式；
- 完整 Release build 与全部保留测试通过。

完成成果：五个测试项目均保留有效的核心契约，Quality Matrix 的项目入口继续有效；删除未被代码读取的旧窗口显示环境变量，未缩小默认测试范围。剩余 TestKit helper 和 TestAssets 均有实际引用，没有遗留 Visual Review 截图设施或临时脚本。`dotnet restore --locked-mode -r win-x64`、`dotnet format --verify-no-changes --no-restore`、`dotnet build -c Release --no-restore` 与 `dotnet test -c Release --no-build` 均通过，Release build 0 warning，所有测试均无 skip；WPF 隔离 Desktop 检查保持 fail closed。首次全量测试曾报告一次 WPF collection cleanup 异常并以非零码退出，随后详细重跑和规定原命令重跑均通过，尚未复现，作为后续观察风险。

---

# Phase B：诊断导出交互与性能遥测可分析性

## [x] T004（P1）：修正问题诊断会话选择器的目录状态

依赖：T001–T003 已完成。

目标：设置页导出已有问题诊断会话时，第一个 OpenFileDialog 每次直接进入应用 `Diagnostics` 目录，并使用独立 persisted-state profile；随后 SaveFileDialog 不继承该目录/状态，继续使用 Windows 对普通保存位置的记忆。扩展现有通用 file-dialog abstraction，不在 Diagnostics 页面直接创建平台对话框。

完成成果：通用文件对话框选项新增可选初始目录和状态 GUID，WPF 层映射到系统对话框；旧诊断会话选择器等待目录加载后，使用当前 Diagnostics 目录及固定独立 GUID，两个 ZIP 保存入口维持普通 Windows 保存状态。Release build、format 和受影响 Presentation focused tests 通过；临时选项语义测试通过后已删除。未恢复已删除的细粒度永久测试；无旧兼容残留或长期文档冲突。人工点击未执行，系统对话框实际显示仍可选验收。

## [x] T005（P0）：提升普通性能遥测的长期可分析性

依赖：T004（仅调度顺序；无技术耦合）。

实施规格：`tasks/T005_performance_telemetry_analysis_readiness.md`

目标：将 CPU/Working Set/Managed Heap 从“每次 Operation 完成时附带采样”改为遥测开启期间约 60 秒一次的独立低频采样；保留空闲资源窗口、process instance 与一分钟时间结构；审计稳定 operation vocabulary；将单个 `telemetry.json` 扩展为 definitions + aggregates + windows 的自解释交换格式，并导出当前 retention 内全部遥测数据。

完成成果：普通遥测改为由可控 `TimeProvider` 定时器驱动的独立低频进程资源采样，开关、关闭、快速重启和系统时钟回拨均保持基线与窗口语义；窗口复用共享 process instance 并兼容缺失身份的旧记录。operation vocabulary 收敛为准确的存储连接打开、缓存音频生成、缓存完整性检查及有限页面表面，耗时 Histogram 覆盖亚毫秒到 60 秒并在导出时按 bucket 定义隔离旧新窗口。`telemetry.json` 现在包含 coverage、collection、metricDefinitions、aggregates 和 windows，并导出 retention 内全部窗口；summary 仅报告客观覆盖、版本、进程、窗口、dropped/degraded 状态。保留并扩展少量核心 Infrastructure/Application 测试，临时测试已删除；T004/T005 focused tests、locked restore、Release build、format 和全量 Release 测试均通过（首次全量运行出现一次已知 WPF cleanup 偶发失败，规定命令重跑通过：Domain 15、Application 162、Presentation 187、WPF 93、Infrastructure 336）。人工 UI 验收未执行，不影响任务完成。无长期文档冲突。

T005 为本阶段收口任务，完成后执行完整 Release 质量门禁。
