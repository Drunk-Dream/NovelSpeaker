# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **Speech Provider 架构重构阶段**。

当前代码基线：`284d2a29d1c4c855228e5c012e55bce4af208d6c`（`dev`）。

上一阶段已经完成测试体系收敛、诊断导出交互修正和普通性能遥测可分析性优化。本阶段将现有 HTTP TTS Rule 模型统一升级为 Speech Provider，并加入实验性的 Microsoft Edge Provider。

本阶段目标：

- 明确区分 Provider Type 与 Provider Instance；
- 用统一 Provider Runtime 替代 Playback/Cache 对 `HttpTtsRule` 的直接依赖；
- 把现有 HTTP 请求能力收敛为 HTTP Provider，并清理 Legado/旧 TTS Rule 兼容包袱；
- 统一 Provider 排序，并让管理页与播放页使用同一顺序；
- 保留成熟缓存系统，以真实合成配置指纹决定缓存可用性；
- 增加实验性单实例 Microsoft Edge Provider，并在开发阶段完成至少一次真实在线合成验证；
- 将现有规则页/Provider 页的拖拽排序统一为单一“插入槽位”交互；
- 完成后删除旧 TTS Rule 顶层模型、旧兼容运行路径和相关旧术语。

长期规则见：

- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `docs/specs/HTTP_TTS.md`
- `AGENTS.md`

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短“完成成果”
- `[!]` 阻塞，记录会影响产品/架构/隐私/路线的真实冲突

默认按 T006 → T010 串行执行。如果调用明确要求连续执行整个 Backlog，可以在每个任务自动验收完成后继续，不等待人工验收。

人工验收永远是可选补充，不阻塞任务完成或下一任务。

每个未完成任务的详细实施合同位于 `tasks/`。完成任务后：

1. 满足 task spec 中的自动验收；
2. 删除所有当前任务临时测试、fixture、脚本、截图和诊断产物；
3. 更新本文件状态与“完成成果”；
4. 删除对应 `tasks/Txxx_*.md`；
5. 不等待人工验收。

---

# Phase A：测试资产收敛

## [x] T001（P0）：审计永久测试并建立保留/删除判定

目标：对现有 Domain / Application / Presentation / Infrastructure / WPF 测试按新的永久测试准入标准分类。重点识别锁定实现细节、重复覆盖、低价值 UI/XAML 结构断言和过细 ViewModel/Architecture contract tests，形成可执行的删除、合并、重写清单。

完成成果：已完成永久测试审计，并按核心契约、高风险边界和实现细节锁定风险完成 KEEP / DELETE / MERGE-REWRITE 分类。

## [x] T002（P0）：按核心契约瘦身现有测试体系

依赖：T001。

目标：执行 T001 的审计结论，删除/合并/重写不符合长期准入标准的测试。

完成成果：已删除 WPF 样式、资源、截图、精确几何与页面结构类低价值永久测试，合并重复 Application/Infrastructure/Presentation 测试并保留核心用户流程、持久化、安全和架构边界。

## [x] T003（P0）：收口质量门禁与后续测试工作流

依赖：T002。

目标：检查 CI/质量门禁与新的长期测试策略一致，清理失去引用的 TestKit/fixture/辅助代码，并执行完整 Release 质量门禁。

完成成果：五个测试项目与 Quality Matrix 保持有效；临时视觉设施已清理；locked restore、format、Release build 和全量测试通过，人工验收保持可选且不阻塞任务。

---

# Phase B：诊断导出交互与性能遥测可分析性

## [x] T004（P1）：修正问题诊断会话选择器的目录状态

目标：旧 `.nsdiag` 选择器直接进入 Diagnostics 目录，并与随后 ZIP 保存对话框的 Windows 目录记忆隔离。

完成成果：通用文件对话框支持独立初始目录和状态 GUID，诊断选择器与普通保存状态完成隔离，相关自动验证通过。

## [x] T005（P0）：提升普通性能遥测的长期可分析性

目标：把 CPU/Working Set/Managed Heap 改为独立低频采样，保留时间窗口、process instance 与稳定 operation vocabulary，并完善自解释导出。

完成成果：低频进程资源采样、约一分钟窗口、definitions + aggregates + windows 导出和 retention 内完整导出已完成，完整质量门禁通过。

---

# Phase C：Speech Provider 重构

## [ ] T006（P0）：建立 Provider 核心模型、持久化与 Runtime 边界

实施规格：`tasks/T006_speech_provider_foundation.md`

目标：建立 Provider Type / Provider Instance、统一排序、CurrentProvider、typed config 与 Provider Runtime；通过一次性 migration 转换可安全映射的旧 HTTP TTS Rule，跳过异常项并向用户报告，不保留长期双读/双写兼容路径。

## [ ] T007（P0）：将 HTTP TTS Rule 收敛为 HTTP Provider

依赖：T006。

实施规格：`tasks/T007_http_provider.md`

目标：保留成熟 HTTP 请求/模板/响应验证能力，建立 NovelSpeaker 自有 HTTP Provider 模板语言、结构化请求频率限制和新版 Provider 导入/导出格式，并清理 Legado 兼容接口和旧 TTS Rule 外部格式。

## [ ] T008（P1）：重构语音服务管理 UI 并统一拖拽排序

依赖：T006–T007。

实施规格：`tasks/T008_speech_service_ui_and_reorder.md`

目标：将设置中的 TTS Rule 页面升级为双栏“语音服务”Provider 管理页；实现统一 Provider 排序；把 Provider、章节规则、正则规则等列表拖拽反馈统一为单一插入槽位。

## [ ] T009（P0）：实现实验性 Microsoft Edge Provider

依赖：T006、T008。

实施规格：`tasks/T009_edge_provider.md`

目标：增加内置单实例 Edge Provider、实验功能生命周期、Voice 搜索/配置/试听和独立 Infrastructure transport；以至少一次真实在线合成证明 transport 可用，不引入外部代理进程或自动 fallback。

## [ ] T010（P0）：完成 Playback/Cache/Export Provider 接入并清理旧体系

依赖：T006–T009。

实施规格：`tasks/T010_provider_playback_cache_cleanup.md`

目标：让 Playback、Prefetch、Active Cache、Coverage、Export 和播放页全部以 Provider 为唯一语音服务模型；完成 ProviderSynthesisFingerprint、播放页 Provider 选择器和旧 TTS Rule 代码/术语清理；最后执行完整 Release 质量门禁。

T010 为本阶段收口任务。
