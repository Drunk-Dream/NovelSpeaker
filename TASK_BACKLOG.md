# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **Speech Provider 架构重构阶段**。

当前代码基线：`284d2a29d1c4c855228e5c012e55bce4af208d6c`（`dev`）。

上一阶段已经完成测试体系收敛、诊断导出交互修正和普通性能遥测可分析性优化。本阶段将现有 HTTP TTS Rule 模型统一升级为 Speech Provider，并加入实验性的 Microsoft Edge Provider。

本阶段目标：

- 明确区分 Provider Type 与 Provider Instance；
- T006 暂时断开的 TTS 调用链由后续任务重新接通：T007/T009 提供并注册各 Provider Runtime，T010 将 Playback/Prefetch/Active Cache 接回 Runtime，并让 Coverage/Export 使用 Provider 与 fingerprint 合同；
- 用统一 Provider Runtime 替代 Playback/Cache 对 `HttpTtsRule` 的直接依赖；
- 把现有 HTTP 请求能力收敛为 HTTP Provider，并清理 Legado/旧 TTS Rule 兼容包袱；
- 统一 Provider 排序，并让管理页与播放页使用同一顺序；
- 保留成熟缓存系统，以真实合成配置指纹决定缓存可用性；
- 建立通用“实验性功能”次级设置页，第一项为实验性单实例 Microsoft Edge Provider；
- Microsoft Edge 协议实现参考 `Drunk-Dream/ms-ra-forwarder` 当前真实协议行为，并在开发阶段完成至少一次真实在线合成验证；
- 将公共 SpeakSpeed 从旧 `1–20` 直接切换为 Provider 无关的 `0–100`，默认值 `50`，不保留旧语速兼容层；
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

## [x] T006（P0）：建立 Provider 核心模型、持久化与 Runtime 边界

目标：建立 Provider Type / Provider Instance、统一排序、CurrentProvider、typed config 与 Provider Runtime；v7→v8 只新增长期需要的 Provider 表，将可转换的旧 HTTP TTS Rule 迁入并静默丢弃不可转换项，删除不再需要的旧表，不新增一次性报告表。T006 后语音相关入口可暂时不可用，T010 后整体恢复可用。

完成成果：Provider 核心契约、SQLite v8 持久化与旧 HTTP Rule 一次性迁移、CurrentProvider 设置恢复重试和 Runtime/Resolver 边界已完成；无效旧项静默丢弃，旧表随迁移事务删除。

## [x] T007（P0）：将 HTTP TTS Rule 收敛为 HTTP Provider

依赖：T006。

目标：保留成熟 HTTP 请求/模板/响应验证能力，建立 NovelSpeaker 自有 HTTP Provider 模板语言、结构化请求频率限制和新版 Provider 导入/导出格式，并清理 Legado 兼容接口和旧 TTS Rule 外部格式。

完成成果：HTTP Provider Runtime 已注册并复用安全模板、限流、HTTP 传输和音频验证；支持 Draft 试听、本地保存与编辑、逐项导入及凭据提示后导出 schemaVersion 1 Provider 文件。Legado 解析/转换、旧规则导入和外部序列化已删除；旧规则页导入入口移除，新语音服务管理 UI 由 T008 接入。

## [x] T008（P1）：重构语音服务管理 UI 并统一拖拽排序

依赖：T006–T007。

目标：将设置中的 TTS Rule 页面升级为双栏“语音服务”Provider 管理页；实现统一 Provider 排序；把 Provider、章节规则、正则规则等列表拖拽反馈统一为单一插入槽位。

完成成果：双栏语音服务页已接入 Provider 列表、HTTP Draft/编辑/试听/保存、新身份/唯一副本名/紧随源项的事务复制、凭据确认后单项导出及逐项导入；编辑选择与 CurrentProvider 状态独立，删除正在使用的服务清空选择且不回退。Provider 排序事务持久化；Provider、章节规则和正则规则共用列表级单一插入槽与边缘滚动，纯逻辑覆盖首尾、相邻槽及隐藏项映射。旧 TTS 管理页和 Toggle/旧限流输入已移除，核心流程、持久排序与隔离桌面临时交互验证通过；临时验证代码已清理。额外扩大运行时，未改动的空白 Frame 高度测试在隔离环境中超时；受影响的页面生命周期、设置导航和工作台高度测试均通过。

## [x] T009（P0）：实现实验性 Microsoft Edge Provider 与统一语速合同

依赖：T006、T008。

目标：建立通用“实验性功能”次级设置页；增加内置单实例 Microsoft Edge Provider、Voice 搜索/配置/试听和独立 Infrastructure transport；协议行为以当前 `Drunk-Dream/ms-ra-forwarder` 为主要参考；将公共 SpeakSpeed 直接切换为 `0–100`、默认 `50`，不迁移旧 settings/template 语速语义；以至少一次真实在线合成与可解码验证证明 transport 可用，不引入本机 Edge 依赖、外部代理进程或自动 fallback。

完成成果：通用 FeatureId 设置与“实验性功能”次级页已接入；Edge 固定单实例在首次启用时创建，停用保留配置/排序并原子清空必要的 CurrentProvider。独立 Edge Editor 支持离线 Voice 快照、内联搜索、绕过缓存刷新和未保存 Draft 试听；Catalog 仅在进程内缓存，失败/缺失 Voice 不取消已配置状态，加载不锁死编辑器。Infrastructure 使用固定 `edge-readaloud-144-v1` profile 和独立 WebSocket；2026-09-29 生产 Voice List、MP3 合成及现有解码路径验证成功。语速统一为 0–100/默认 50，移除 0 sentinel 与 HTTP 专属试听服务，不迁移旧设置或模板。核心生命周期、持久化、Runtime/协议、语速及 Draft 测试通过，Release build、format 和隔离桌面验证通过；临时验证入口/产物及本任务规格已删除。扩大检查时 Domain 15、Infrastructure 335、Presentation 206 项全通过；Application 197 项通过，4 项章节标题相关导出失败在任务起点 `2ddec3c` 同样复现，保留为已有问题。播放/缓存/导出生产接线与播放页 Provider 选择器由 T010 完成。

## [x] T010（P0）：完成 Playback/Cache/Export Provider 接入并清理旧体系

依赖：T006–T009。

目标：将 T006 暂时断开的 TTS 生产调用链接回已注册的 Provider Runtime，让 Playback、Prefetch、Active Cache、Coverage、Export 和播放页全部恢复 Provider 语音服务能力；完成 ProviderSynthesisFingerprint、播放页 Provider 选择器和旧 TTS Rule 代码/术语清理；全链路遵循 T009 已建立的 `SpeakSpeed 0–100` 合同并正确支持合法值 `0`；最后执行完整 Release 质量门禁。T010 验收前不得留下仅有接口/模型、生产调用方仍未接通的状态。

T010 为本阶段收口任务。

完成成果：Playback、Prefetch 和 Active Cache 已接通已注册的 HTTP/Edge Runtime；当前句保持原音频，下一句使用最新选择与已保存配置，缺失/隐藏/未配置/Runtime 不可用时稳定停止且不回退、不错误推进进度。预取逐请求读取最新 Provider/语速，活动缓存冻结 typed config、语速和文本配置。Speech 发布 typed semantic change，由 Cache 决定 Coverage invalidation；Coverage/Export 仅按 Provider synthesis profile 读取可验证缓存，不调用合成 Runtime。播放页选择器只列出已配置可见服务，统一排序、整项当前视觉、None 空选中和管理入口均已接入。

旧顶层 TTS Rule 模型、选择/编辑/测试接口、执行编译器、仓储与用户术语已删除；README 已更新。历史设置读取和既有迁移保留原兼容行为，旧物理缓存不主动删除，没有数据库结构或数据迁移变更。旧模型细节测试移除，重复 fingerprint 测试合并到 Provider 契约测试；保留模板安全、HTTP transport、持久化、缓存/导出及架构核心测试，新增真实生产组合、句子生命周期、冻结配置、最新预取、429 重试、共享预取取消、空音频恢复、前台抢占和暂停音频可用性回归测试。测试 fixture 仅清理自身数据库连接池，滚动断言允许浮点舍入误差；隔离桌面在线程确认退出后对 Win32 170 做有限释放重试，超时和其他错误仍失败关闭并有核心安全测试保护。locked restore、format verify、Release build（零警告）和完整测试通过：Domain 15、Application 200、Infrastructure 314、Presentation 212、隔离桌面 WPF 96，共 837 项。无长期文档冲突，临时产物与本任务规格已清理。

## [x] T011（P1）：修正语音服务卡片并统一输入密度

依赖：T008；本项按用户反馈独立修复，不重新激活 T008。

目标：设置常用入口按常规、播放设置、语音服务排序；语音服务卡片沿用规则卡片的边框和交互语义；帮助按钮与编辑标题同行；从全局样式约定统一收紧文本框、密码框和下拉框。

完成成果：常用入口已按指定顺序排列；Provider 卡片共用 CardItem 表面状态并采用规则卡片几何，帮助按钮移至编辑标题同行。文本框、密码框和下拉框使用全局 Input Token，Standard 为 32 DIP / `10,4`，Compact 为 28 DIP / `8,2`；保留 Stretch 内容布局，文本显式左对齐，多行输入顶部对齐。长期视觉约定同步更新；设置键盘导航核心测试已适配，未新增永久视觉结构测试。focused tests、Release build、format 及隔离桌面 Light/Dark 临时布局和交互验证通过，临时代码和产物已清理。

## [x] T012（P1）：修复 Edge Voice 列表滚动

依赖：T009；本项按用户反馈独立修复，不重新激活 T009。

目标：修正 Voice 列表鼠标滚轮步幅过大，以及列表滚动条与外层编辑页滚动条重叠的问题；保留虚拟化和嵌套滚动边界转交。

完成成果：Voice 列表使用虚拟化像素滚动，Edge 编辑器内容为外层滚动条预留右侧间距。隔离 Desktop 的 Light/Dark、不同窗口宽度及 10,000 条 Voice 临时验证通过，覆盖滚轮步幅、内层优先、顶部/底部边界转交及滚动条分离；临时测试和诊断产物已清理。已有滚轮/工作台 WPF tests 3 项、Voice 编辑流程/架构 Presentation tests 24 项、Release build（0 警告/错误）及 format 通过。保留现有核心测试，未新增永久视觉结构测试；未改动持久化、Provider 行为或长期文档，未执行全量测试。
