# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **整体架构优化阶段**。规划基线：`259541e37e98d0bac87fa41e5017f783b7ee0e31`。

核心功能已经基本完善，本阶段优先减少架构复杂度、状态/生命周期耦合和大列表规模风险，不新增大功能，不围绕旧 3000+ 章节卡顿继续做局部 workaround。

目标终态由 `docs/01_ARCHITECTURE.md` 定义。Codex 负责代码、测试和必要的目录/API 迁移；编号文档已经在规划阶段完成整理，除任务明确要求外不修改编号文档。

本阶段允许：

- 大规模 namespace/目录/internal API 调整；
- DI 生命周期调整；
- 删除旧 abstraction/compat wrapper；
- 测试重写、合并和数量减少；
- 删除已经被新架构替代的代码。

必须保护：

- 用户数据与已发布 SQLite migration；
- 外部 TXT；
- 规则/设置/ReadingProgress；
- 核心用户行为；
- PlaybackSnapshot/checkpoint/navigation/cache/export 等已确认语义。

## 2. 状态

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，末尾追加“完成成果”
- `[!]` 阻塞，记录可复现证据

优先级：

- `P0`：架构基础、状态正确性、生命周期、大列表或阻塞后续 Phase 的任务
- `P1`：重要收敛/清理/测试任务
- `P2`：低风险维护性收尾

完成后的任务保留。只有新的规划阶段才允许再次重写 Backlog。

## 3. 通用执行规则

1. 一次只执行一个编号任务，完成后停止。
2. 先读 `AGENTS.md`、`docs/README.md`、`docs/01_ARCHITECTURE.md` 和当前任务对应专项文档。
3. 先审计真实调用链、DI 注册和现有测试，不按本文文件列表机械修改。
4. 行为保持型 move/rename 尽量与逻辑变化分 commit；如果用户未授权提交则只保持工作区修改。
5. 允许破坏内部 API，但不得为旧内部 API 长期保留 wrapper。
6. 新 interface 必须符合“有真实边界才有接口”；Feature-local controller 默认 concrete internal type。
7. 普通 Page/ViewModel 目标生命周期为 transient；不得用 Singleton/NavigationCache 规避迁移问题。
8. 不引入 EventBus/Messenger、Service Locator、通用 BackgroundTaskManager、大一统 CacheManager 或复杂泛型 Rule Framework。
9. 大列表必须遵守 Immutable Catalog + Sparse Mutable Decoration，禁止重新引入首屏 `Clear + N × Add`。
10. 复杂页面遵守 staged loading；`Task.Yield()` 不作为性能修复完成证据。
11. WPF 自动测试使用隐藏隔离 Desktop；不得设置可见窗口环境变量。
12. 临时 instrumentation、trace、dump、截图、benchmark、一次性脚本在任务完成前删除。
13. 每项任务末尾记录：主要实现、删除的旧实现、测试、ArchitectureTests、无法执行的检查。
14. Codex 默认只修改自身 `TASK_BACKLOG.md` 完成状态，不整理编号文档。

---

# Phase A：架构守卫与生命周期基础

## [x] T001（P0）：建立架构优化阶段 Fitness Tests 基线

目标：先把已经确认的目标架构转成自动约束，避免后续迁移过程中旧模式重新进入代码库。

实施方向：

1. 扩展现有 ArchitectureTests，而不是新建第二套架构测试框架。
2. 至少建立以下可自动检查的约束：
   - Domain/Application/Infrastructure/App 引用方向；
   - App 非 Bootstrap 不直接引用 Infrastructure；
   - Shared 不引用 Feature；
   - App Feature namespace 不形成双向依赖；
   - ordinary Page/ViewModel 不允许新注册为 Singleton；
   - ViewModel/Feature controller 不依赖 `IServiceProvider`；
   - Application 不引用 WPF/Wpf.Ui；
   - 禁止新增通用 Messenger/EventBus 类型和 Service Locator；
   - 页面/ViewModel 不直接依赖 ReadingProgress persistence writer；
   - 对已识别 large-list helper 增加“不得 Clear 后逐项 Add”结构性合同。
3. 对“Playback mutable state 只有 owner 修改”先建立可稳定检测的最小边界，不依赖脆弱私有字段名；如果当前结构暂时无法精确检查，先约束公开/注入方向，并记录后续 T008/T009 补强点。
4. 不建立行数、构造参数数量、绝对耗时等机械门槛。

禁止：

- 为了让当前旧代码通过而把规则写得过于宽松；如果现状明确违反目标规则，允许对该规则使用“已知债务白名单”，但白名单必须精确到具体类型/依赖，并注明由哪个后续任务删除。
- 新增第三方架构测试框架，除非现有测试无法表达必要规则且能证明收益。

验收：

- Architecture tests 可单独运行并稳定通过。
- 每个临时白名单都能映射到 T002–T018 的明确删除任务。
- 无生产行为变化。

完成成果：扩展现有 ArchitectureTests，覆盖层间依赖、Feature 循环、DI 生命周期、Service Locator、ReadingProgress、大列表和 Playback owner 边界；保留精确到文件/类型的 T002、T003、T005 临时债务白名单，并补充 XAML、factory 和控制流合同测试。

## [x] T002（P0）：统一普通 Page/ViewModel 为 transient 生命周期

依赖：T001。

目标：消除 singleton ViewModel + page activation + CTS/version 的混合生命周期；真正长期状态保留在 process/session owner。

实施方向：

1. 审计所有 Page/ViewModel DI 注册，形成当前 Singleton/Transient 清单。
2. 将 Library、Player、TtsRules、ChapterRules、Settings 等普通页面 VM 迁移为 transient；保留真实 process owner 为 Singleton。
3. 对每个被迁移 VM 逐项判断原 singleton 中哪些状态是：
   - 页面临时 state：随 VM 销毁；
   - 真实长期 state：提升/复用现有 Application/process owner；
   - 可由 query/snapshot 重建：不新增缓存。
4. 统一 activation/deactivation：一个 page activation token/version + owned tasks；删除因为 singleton 历史产生的重复“是否已订阅/是否已初始化”防御状态。
5. 保持播放、主动缓存、导出、设置当前 snapshot 等跨页面状态连续。

重点审计：

- `PlayerViewModel`
- `LibraryViewModel`
- `TtsRulesViewModel`
- `ChapterRulesViewModel`
- `SettingsViewModel`
- Shell/Page provider 的生命周期假设

禁止：

- 通过 Page singleton、NavigationCache 或静态字段保存页面状态。
- 新建通用 `PageStateCache`。

测试：

- 进入→离开→重新进入得到新 VM 实例。
- process owner 状态不因 VM 重建丢失。
- 旧页面迟到任务/事件不能写入新实例。
- DI/ArchitectureTests 更新并通过。

完成成果：6 个普通 Feature ViewModel 改为 transient；Library/Player 页面事件与缓存刷新统一在 activation 中挂接；保留 LibraryScrollState、Playback session、自动滚动等 process/session owner，并补充 DI 生命周期与页面生命周期合同测试。

## [x] T003（P0）：重组 App Feature 目录/namespace 并消除跨 Feature 循环

依赖：T002。

目标：按 Books/Playback/Cache/Rules/Settings/Diagnostics 收敛 App Feature，使目录和 namespace 直接体现目标边界。

实施方向：

1. 优先做行为保持型 move/rename，再处理因边界调整产生的调用合同。
2. 建立：
   - `Features/Books/Library`
   - `Features/Books/Details`
   - `Features/Books/Shared`
   - `Features/Rules/Tts|Chapter|Regex|Shared`
3. 消除 `Library ↔ BookDetails` 直接类型/namespace 双向引用：
   - route 继续使用 Shell typed route；
   - 真正共享的 book presentation primitive 放 `Books/Shared`；
   - 不把整个 VM/页面 DTO 提升到全局 Shared。
4. 三类 Rules 先只做目录/namespace 收敛，不在本任务实现完整共享 editor 生命周期。
5. 清理迁移后的旧 namespace、using、空目录和 compatibility alias。

禁止：

- 建立 `Shared/Everything`；
- 为保留旧 namespace 留 forwarding type；
- 在同一任务顺带重构 Playback/Cache 业务逻辑。

验收：

- Feature dependency tests 无双向引用。
- 生产行为保持，相关 Presentation/WPF tests 通过。
- 无旧 namespace compatibility bridge。

完成成果：Books 已收敛为 Library/Details/Shared，Rules 已收敛为 Tts/Chapter/Regex/Shared；书籍共享封面、进度投影、目录失效和删除对话合同迁入 Books/Shared，清除 Library↔BookDetails 循环及旧 namespace；Release 编译、Presentation 全量测试、架构守卫和 Books/Rules 相关 WPF 测试通过。

---

# Phase B：Books、Query 与大列表架构

## [x] T004（P0）：拆分 Books CQRS-style read model 与查询边界

依赖：T003。

目标：替代过宽的 `GetBookDetailsAsync`/页面 aggregate 查询，使 Library、Details、Playback 获取场景化 immutable read model。

实施方向：

1. 审计 `IBookLibraryQuery`、`BookLibraryQuery`、Playback metadata query 和调用方。
2. 建立最小稳定 query 集合，优先包括：
   - Library summaries；
   - Book header；
   - Book catalog；
   - persisted reading position（如现有 read model 已包含则按语义保留）；
   - 单章内容/metadata；
   - 必要统计的独立查询。
3. Book catalog 只返回稳定轻量字段，不携带 cache percentage 等动态 UI decoration。
4. SQL 限制到目标 BookId/场景；对现有 header 全局聚合形状做 query-plan 验证，低风险时一并收敛。
5. 保持 SQLite schema/migration 不变，除非 query 优化确有新 index 必要；若需新 index，只追加 migration 并有升级测试。
6. 迁移调用方后删除旧宽 query/DTO，不保留兼容 wrapper。

测试：

- Infrastructure 集成测试覆盖 query 语义、排序和空数据。
- 记录关键 query plan，确保单书查询不因改造退化为 N+1。
- Application/App 调用方只依赖新 read model。

完成成果：拆分 Library summaries 与 BookDetails header/catalog/reading position/statistics 查询及 immutable read model，BookDetails 改为并行加载场景化查询；删除旧 `BookDetails` aggregate、旧宽查询入口和动态 `IsCurrent` 字段；补充空目录、查询排序、独立投影及目录顺序进度投影测试，相关 Presentation/WPF/Infrastructure focused tests 与 ArchitectureTests 通过。

## [x] T005（P0）：建立大型 Catalog 与 Sparse Decoration 基础设施

依赖：T004。

目标：为 BookDetails/Player/CacheManagement 提供统一但不过度抽象的大列表 presentation 模式。

实施方向：

1. 设计轻量 immutable catalog item/list，不要求所有列表共用同一个泛型框架。
2. 提供稳定的 index/id lookup，使 current item O(1) 定位。
3. 动态 decoration 只保存真正会变化的少量状态，并支持按 index/id 更新。
4. 设计批量 UI 提交语义：初始 catalog 一次提交/替换，不使用 `Clear + N × Add`。
5. current/selection/cache decoration 更新不得遍历整个 catalog 才找到目标。
6. 如果需要共享 collection primitive，范围只到“批量替换 + 稳定 lookup”，不要做万能虚拟列表框架。

禁止：

- 为 10,000 项预建复杂 mutable VM 作为唯一模型。
- 新增第三方 virtualization/data-grid 框架。
- 把 WPF container 存进业务/presentation state。

测试：

- 10,000 item 纯 presentation 测试验证初始提交不是 N 次 Add。
- current old/new 只产生有界 decoration 变化。
- lookup 不通过全表线性扫描。

完成成果：为 BookDetails、Player、CacheManagement 引入 immutable catalog、Sparse Decoration、稳定 key/position lookup 与分批 collection projection；CacheManagement 使用结构目录和可视窗口 decoration，Player/BookDetails 的 current、selection、cache 更新均走有界索引；补充取消、生命周期清空、过期 viewport 结果、刷新失败、目标查询、10,000 项 projection 与 WPF realized-row/viewport 回归测试。

## [ ] T006（P0）：迁移 Library 到新 Query/生命周期/批量 Projection

依赖：T005。

目标：Library 完成新架构迁移，成为 Books Feature 的第一个完整样板。

实施方向：

1. 使用 Library summaries query 和 immutable read model。
2. 搜索/排序在非 WPF 数据结构中计算，然后批量提交可见 projection。
3. matching PlaybackSnapshot 只更新对应书籍进度 decoration，不重新查询/重建整个 Library。
4. 保留当前响应式卡片布局和产品交互，不做视觉改版。
5. 删除旧 singleton/重复 progress projection/过渡 helper。

测试：

- transient VM 重建；
- search/sort；
- matching/cross-book Snapshot；
- 大书库批量 projection；
- WPF 响应式布局合同不回归。

## [ ] T007（P0）：迁移 BookDetails 到 Catalog + Staged Loading

依赖：T006。

目标：彻底移除 BookDetails 当前“全量复杂 item + 全量 cache/status + locator/layout”耦合，按目标架构重建页面数据流。

实施方向：

1. Critical 阶段：Book header + immutable chapter catalog + effective reading position。
2. 首个 interactive frame 完成后再启动：
   - current chapter locator；
   - current/viewport cache decoration；
   - 次级统计。
3. `BookDetailsViewModel` 缩小为页面 state/command/activation；查询、编辑 draft、reading projection、catalog/decorations 使用 Feature-local controller/projector。
4. Cache status 只查询 current/viewport/明确受影响 chapters；CacheChanged 只更新目标 index。
5. locator 使用 catalog index/id 直接目标定位，完成后解除临时 readiness/layout 监听。
6. 页面离开后旧 enrichment/locator 不写回。
7. 删除旧 `ResettableObservableCollection`/initial-cache projection workaround 等仅为旧模型存在的代码（若已无其它真实调用方）。

禁止：

- Page cache/Singleton；
- 固定 Delay；
- 全量 cache status 首屏加载；
- 把旧 item VM 再包一层 facade 继续保留。

测试：

- 10,000 catalog 的 Presentation 结构回归；
- first-frame gate 不等待 cache enrichment；
- current begin/middle/tail；
- fast leave/re-enter cancellation；
- matching Snapshot 与 persisted fallback；
- WPF locator/virtualization focused tests。

---

# Phase C：Playback Core

## [ ] T008（P0）：抽取 PlaybackSessionState 与 CommandProcessor

依赖：T007。

目标：在不改变唯一 session owner 的前提下，把 `PlaybackCoordinator` 的 canonical mutable state 与命令提交边界显式化。

实施方向：

1. 先用现有测试固定 Start/Open/Pause/Stop/Jump/Move/session replacement 行为。
2. 建立内部 `PlaybackSessionState`（或等价命名），只由 Playback owner/CommandProcessor 修改。
3. 抽取 `PlaybackCommandProcessor` 负责命令串行化、目标解析后的提交、失败/取消恢复和 event epoch 检查。
4. `PlaybackCoordinator` 保持外部 facade/ports 和 SnapshotChanged 入口，逐步委托内部组件。
5. 不改变 Snapshot/checkpoint 用户语义。
6. 迁移后删除 Coordinator 中重复 state/command helper，不通过 partial class 假拆分。

ArchitectureTests：补强“session mutable state 只能由指定 owner 修改”的可检测约束。

测试：覆盖当前 PlaybackCoordinator 全部核心命令、stale audio event、失败/取消和快速切换。

## [ ] T009（P0）：拆分 Playback Audio、Progress 与 StopTimer

依赖：T008。

目标：把资源生命周期、checkpoint 和 timer 从 Playback facade/command 逻辑中独立为受 Session owner 管理的组件。

实施方向：

1. `PlaybackAudioController` 唯一持有 Local audio/NAudio 资源和 callback bridge。
2. `PlaybackProgressController` 统一 explicit jump、Pause、Stop、replacement、shutdown checkpoint，并保持 stale-session 防覆盖。
3. `PlaybackStopTimer` 独立 TimeProvider/CTS，不复制播放状态。
4. 所有组件由 `PlaybackCoordinator`/Session owner 持有和协调，外部 VM 不直接组合这些内部组件。
5. 删除旧 coordinator 内对应重复字段/helper。

测试：资源释放、audio callback、checkpoint、timer replacement/trigger、shutdown。

## [ ] T010（P0）：拆分 Playback ContentResolver 与 PrefetchCoordinator

依赖：T009。

目标：把正文读取/文本处理/speech plan/segment compose 与 prefetch 生命周期从主 Session 逻辑中收敛。

实施方向：

1. ContentResolver 负责 chapter content → regex/text profile → speech plan → segments。
2. PrefetchCoordinator 只拥有 playback-session prefetch，不与 ActiveCache owner 混合。
3. Current playback 与 Prefetch 继续共用 TTS admission/cache 能力。
4. 规则/速度/文本配置变化的 invalidation 语义保持现有合同。
5. 删除旧 coordinator/content service 中重复 orchestration，避免建立第二个万能 service。

测试：content resolution、plan version、prefetch cancellation/priorities、session replacement。

---

# Phase D：Player Presentation

## [ ] T011（P0）：将 PlayerViewModel 重构为 transient presentation facade

依赖：T010。

目标：保留单一 XAML DataContext，但把 1500+ 行 VM 的职责拆到 Feature-local controller。

实施方向：

1. 建立/收敛：
   - Playback projection；
   - Content controller；
   - Speech control controller；
   - Cache decoration controller；
   - Interaction/scroll controller。
2. PlayerViewModel 只组合绑定 state、commands、activation 和 controller 生命周期。
3. controller 默认 internal concrete class，不建立 Application port。
4. Playback 连续性全部来自 Playback owner，VM 重建不产生第二套 session state。
5. 删除旧 VM 中迁走的 event/version/CTS/helper。

测试：Player Presentation tests 按 controller/VM 合同重新分层；允许删除与旧私有结构绑定的测试。

## [ ] T012（P0）：迁移 Player 章节目录到 Catalog/Decoration 架构

依赖：T011。

目标：Player 的全书章节 metadata 使用与 Books 一致的规模化 catalog 原则。

实施方向：

1. 复用稳定 Books catalog/read model 边界，不复制第二套 chapter model。
2. current chapter/segment decoration 只更新必要项。
3. cache status 只对 current/viewport/明确变化章节刷新。
4. current chapter locator 不线性寻找全部 items。
5. 保留用户主动定位、正文自动居中和虚拟化交互。

测试：10,000 chapter Presentation、begin/middle/tail locator、snapshot rapid changes、manual scroll/locate WPF tests。

---

# Phase E：Cache

## [ ] T013（P0）：建立 CacheCatalog/CacheStore 边界并拆解 CacheWorkspaceService

依赖：T012。

目标：明确“查询状态”“物理 cache”“主动缓存 batch”“页面 workspace”的所有权，删除过宽 facade。

实施方向：

1. 审计 `CacheWorkspaceService`、`AudioCacheFacade`、`SqliteAudioCacheIndex`、speech plan/status query、ActiveCache 调用链。
2. 把页面/业务查询统一到 CacheCatalog/read model。
3. CacheStore 只负责 index/file 原子操作、lease/protection/验证。
4. ActiveCacheCoordinator 继续独立 batch owner。
5. 页面选择/filter 不再进入 process cache service。
6. 迁移调用方后删除无价值 workspace API 和重复 Changed event。
7. 保留现有 cache identity、计划补建和清理语义。

禁止：大一统 CacheManager、通用 BackgroundTaskManager。

测试：cache query/store、active batch、status event/version、删除/清理/lease。

## [ ] T014（P0）：迁移 CacheManagement 到 transient + scalable catalog

依赖：T013。

目标：页面 filter/selection 与 process cache/export state 分离，并消除 full Clear + Add 列表路径。

实施方向：

1. VM transient。
2. Cached book/chapter 使用场景化 immutable read model。
3. 初始列表批量提交，不逐项 Add。
4. selection 使用 id/index state，不依赖 WPF container。
5. export preparation 只提交 immutable batch 参数给 ChapterExportCoordinator。
6. active cache/export snapshot 只做 UI projection。
7. 删除旧 workspace selection/batch coupling。

测试：大 cached chapter set、selection/filter、页面离开、后台任务继续、导出提交。

---

# Phase F：Rules 与 Settings

## [ ] T015（P1）：建立 Rules Shared 编辑生命周期

依赖：T014。

目标：提取三类 Rules 真正重复的 editor lifecycle，而不统一业务模型。

实施方向：

1. 对 TTS/Chapter/Regex VM 做重复逻辑对照。
2. 建立 Feature-local/shared：EditorSession<TDraft>、selection、reorder、common import result 等最小组件。
3. Shared 只处理 selected/draft/dirty/save-cancel/reorder 等生命周期。
4. validation、default、preview/test、persistence DTO 仍留各规则 Feature。
5. 不建立继承层次深的 generic base VM。

测试：共享生命周期组件使用纯 Presentation tests；三类规则各保留业务特有测试。

## [ ] T016（P1）：迁移 TTS/Chapter/Regex Rules 并删除重复实现

依赖：T015。

目标：三套规则页面完成 transient + shared editor 生命周期迁移。

实施方向：

- VM transient；
- 复用 shared editor session；
- 保持启用状态、当前 TTS 规则、导入/导出、排序、ContextMenu 语义；
- 删除旧 duplicated draft/dirty/reorder/import orchestration；
- 不为旧测试保留兼容 API。

测试：允许显著精简重复 fixture；WPF tests 只留真正控件/拖动/ContextMenu 契约。

## [ ] T017（P1）：迁移 Settings 为 process snapshot + transient 页面 VM

依赖：T016。

目标：清除 Settings singleton VM 历史，明确即时设置与 draft 设置。

实施方向：

1. Settings process service 是唯一当前设置 owner。
2. 所有 Settings Page/ViewModel transient。
3. 即时设置直接写 Application settings service；需要保存的设置使用页面 draft。
4. Theme/System/Light/Dark 快捷入口继续使用同一 process setting。
5. 删除页面缓存/初始化标志/重复 Changed subscription。

测试：页面重建、即时设置持久化、draft cancel/save、主题 projection。

---

# Phase G：接口、Shared、测试与代码清理

## [ ] T018（P1）：清理 Application ports、Shared helpers 与 compatibility code

依赖：T017。

目标：在主要迁移完成后做一次真正的“无历史包袱”接口/目录清理。

实施方向：

1. 重新统计 Application interface 与单实现 port。
2. 对每个候选按“技术边界/owner role/cross-feature/external side effect”判断保留价值。
3. Feature-local 单实现 orchestration 改 concrete internal class。
4. 删除已无调用的 wrapper、adapter、obsolete API、旧 namespace helper、duplicate projector/controller。
5. Shared 中只保留真实跨域能力；单 Feature 使用的移回 Feature。
6. 清理 DI alias/重复注册。

禁止：按接口数量机械追求某个目标值。

ArchitectureTests 必须无临时白名单或只剩有明确长期理由的极少数例外。

## [ ] T019（P1）：重构测试体系并减少重复维护面

依赖：T018。

目标：让测试与新架构层级一致，允许测试数量明显减少。

实施方向：

1. Presentation 不再重复 WPF 非视觉行为。
2. WPF tests 只保留 navigation/binding/virtualization/focus/popup/layout/scroll/style。
3. 合并重复 fake/stub，拆大 fixture/support 文件。
4. 删除绑定旧 internal class/compat API 的测试。
5. 保留核心行为矩阵和 Architecture Fitness Tests。
6. 清理 TestKit 仅为旧架构存在的 helper。

验收：

- 测试总数可减少，但必须给出“删除了哪些重复/实现细节测试、哪些稳定合同仍覆盖”的摘要。
- 不能通过合并多个无关 assertion 到单个测试人为压数量。

## [ ] T020（P1）：全项目 dead code / legacy namespace / duplicate state 清理

依赖：T019。

目标：在性能验收前清除本轮迁移产生或暴露的所有旧代码。

检查：

- dead code；
- unused interface；
- legacy namespace；
- compatibility wrapper；
- duplicate state owner；
- orphan DI registration；
- old page lifecycle helper；
- duplicate cache/progress projection；
- 无真实调用方的 Shared helper；
- 临时 TODO/Obsolete/diagnostic flag。

完成后运行完整 ArchitectureTests + Release build + 相关全量非 WPF/WPF tests。

---

# Phase H：真实规模性能验收

## [ ] T021（P0）：执行 180/1000/3000+/10000 章节架构性能验收

依赖：T020。

目标：此时才重新验证此前 3000+ 章节 Player→BookDetails 冻结，并判断新架构是否自然消除了旧瓶颈。

诊断 fixture：

- 180；
- 1000；
- 3000 或 3200；
- 10000。

current position：

- beginning；
- middle；
- tail。

场景：

- Library → BookDetails；
- BookDetails → Player → Back → BookDetails；
- Player chapter catalog；
- CacheManagement 大列表；
- continuous scroll/current locator/cache decoration。

记录：

- first interactive frame；
- max Dispatcher heartbeat gap；
- query/materialization；
- catalog projection/notification count；
- locator；
- cache decoration；
- generated WPF containers；
- GC/allocation/memory（可可靠采集时）。

规则：

- 先判断是否仍有实际瓶颈；新架构已解决则不新增 workaround。
- 如果仍有瓶颈，用 A/B 和 profiling 精确定位后在本任务内做最小架构一致修复，或如影响范围过大标 `[!]` 并记录下一规划问题。
- 不建立固定绝对毫秒 CI 门槛；保留结构性回归测试。
- 所有诊断 harness/trace/script 在任务结束前删除。

## [ ] T022（P1）：最终质量门禁与架构收口

依赖：T021。

目标：完成架构优化阶段最终收口。

必须执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

并检查：

- Architecture Fitness Tests 无临时债务白名单；
- 无 compatibility wrapper/legacy namespace/dead code；
- 普通 Page/ViewModel 生命周期符合目标；
- Playback/Cache state owner 唯一；
- large-list helper 无首屏 Clear + N×Add；
- 编号文档与最终实现无实质冲突（如发现冲突只记录，不在本任务自行重写架构文档）；
- 无 trace/dump/screenshot/TestResults/一次性脚本残留；
- `git status --short` 只包含预期改动。

完成成果应总结：

- 新架构关键边界；
- 删除的主要旧抽象；
- 测试数量与分层变化；
- 真实规模性能结果；
- 仍存在但不阻塞发布的风险。
