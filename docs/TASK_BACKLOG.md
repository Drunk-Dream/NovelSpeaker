# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **Application 模块边界收敛阶段**。规划基线：`1ee1a094393afab4bebc041e8e0374ea54730488`。

上一轮整体架构优化、Rules/Settings 收敛、Cache workspace 兼容层清理、真实规模性能验收和全量测试稳定性修复均已完成。本轮不再进行全面架构重构，只处理会影响后续长期维护以及日志/性能遥测/诊断系统接入的高优先级模块边界问题。

本轮目标：

1. 把 Cache 从历史上的 `Playback` 子系统归属提升为一级 Application 模块；
2. 消除 Settings/TTS/Regex 等源模块对 Cache invalidation 的反向依赖；
3. 用 Architecture Fitness Tests 固定 Application 内部模块依赖方向；
4. 仅在存在明确职责混合时收窄少量过宽 orchestration；
5. 迁移完成后彻底清理旧 namespace、兼容层、重复 DTO/controller、DI alias 和测试遗留。

**本轮明确不实现日志、性能遥测、诊断会话、诊断导出或对应 UI。** 这些能力留到下一轮，在本轮稳定后的模块/生命周期边界上建设。

必须保护：

- 用户数据与已发布 SQLite migration；
- 外部 TXT；
- Rules/Settings/ReadingProgress；
- PlaybackSnapshot、checkpoint、navigation、cache、active cache、export 的既有行为；
- 已确认的大列表与 WPF virtualization 结构；
- 当前全量测试行为合同。

## 2. 状态

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，末尾追加“完成成果”
- `[!]` 阻塞，记录可复现证据

优先级：

- `P0`：必须完成的模块边界/依赖方向/状态 owner 工作；
- `P1`：有明确收益但不值得扩大重构面的职责收敛；
- `P2`：仅清理性工作，本轮原则上并入对应迁移任务或最终收口。

完成后的任务保留并标记完成；只有下一次规划阶段才允许再次清空/重写 Backlog。

## 3. 通用执行规则

1. 一次只执行一个编号任务，完成后停止。
2. 开始前阅读 `AGENTS.md`、`docs/01_ARCHITECTURE.md`、`docs/02_RUNTIME_AND_STATE.md`、`docs/03_DATA_AND_PERSISTENCE.md`、`docs/10_DECISIONS.md` 和当前任务。
3. 先审计真实调用链、namespace、DI 注册、状态 owner 与测试，不按任务中示例文件机械移动。
4. 行为保持型 move/rename 与语义变化尽量分清；允许同一任务多 commit，但任务完成前必须达到最终边界。
5. 内部 API 不要求兼容。禁止为迁移保留 forwarding type、旧 namespace wrapper、alias interface 或 Obsolete bridge。
6. 唯一必须保留的兼容是已发布用户数据、SQLite migration、持久化格式和明确外部合同；不要为内部 namespace 变化新增数据库 migration。
7. 新 interface 仍遵循“存在真实技术/owner/跨模块边界才建立”；Feature-local controller 默认 internal concrete type。
8. 不引入通用 EventBus/Messenger、Service Locator、CommandBus、BackgroundTaskManager 或大一统 CacheManager。
9. 模块之间使用 typed snapshot/event/change source/role port；事件只描述源模块自身语义，不携带目标模块副作用命令。
10. 每个迁移任务完成前必须清理：旧 namespace、旧目录、旧 DI registration、compat code、重复 DTO/controller、旧测试 double、临时 Architecture whitelist、TODO/Obsolete 和一次性诊断产物。
11. 不为减小文件、构造参数或行数机械拆类。只有独立变化原因、独立生命周期或明确边界才允许抽取。
12. 不改变 WPF virtualization、大列表 Catalog/Decoration、staged loading 等上一轮已稳定的架构。
13. 本项目所有文本文件统一 LF；不得因换行符无关地批量重写。
14. 每项任务完成后至少运行对应 focused tests + ArchitectureTests；最终任务执行完整 Release 门禁。
15. 任务完成成果记录：最终 owner/依赖方向、删除的遗留、测试结果、未执行检查和剩余风险。
16. 本轮不要顺带实现或预埋日志、性能遥测、诊断会话/导出框架；只需保证最终模块边界便于下一轮接入。

---

# Phase A：Application 模块边界守卫

## [x] T001（P0）：建立 Application 模块依赖 Fitness Tests

目标：在开始 namespace/owner 迁移前，把本轮最终模块方向转成自动架构约束，并精确记录当前需要由后续任务消除的债务。

实施方向：

1. 扩展现有 `ArchitectureTests/ArchitectureRules`，不要建立第二套架构测试框架。
2. 将 Application 概念模块至少识别为：
   - Books；
   - Speech；
   - Cache；
   - Playback；
   - Settings；
   - Desktop。
3. 建立可长期维护的 namespace/source dependency graph 检查，至少守护：
   - Application 模块不得形成 cycle；
   - Books/Speech/Settings 不允许依赖 Cache-specific invalidation、Coverage、store API；
   - Cache 不允许依赖 Playback session state/commands；
   - Playback 可以依赖 Cache 的稳定 role/query port；
   - Desktop 不拥有 Playback/Cache mutable truth。
4. 对当前尚未迁移的历史依赖允许使用**精确到文件/类型/边**的临时 baseline，但每一项必须注明由 T002 或 T003 删除。
5. 不通过简单禁止整个 namespace 的方式误伤合理稳定的跨模块 read/query contract；先审计真实依赖再编码规则。
6. 不增加第三方架构测试框架。

验收：

- 当前基线在明确债务 baseline 下稳定通过；
- 每个临时债务都有后续删除任务；
- 原有四层、Feature、Playback owner、大列表等 ArchitectureTests 不弱化。

完成成果：扩展现有 ArchitectureRules，识别六个 Application 模块并守护模块 cycle、Books/Speech/Settings → Cache、Cache → Playback、Desktop → Playback/Cache mutable truth 边界；新增精确到文件/类型/边且标注 T002/T003 的债务 baseline，并覆盖 namespace、alias、global using、static member 等引用形式。本任务未删除旧实现或 compat wrapper，遗留边由 T002/T003 清理。验证：locked restore、format、Release build（0 warning/0 error）、全量 Release test（951/951）均通过；未执行检查：无。剩余风险：baseline 中的已知历史依赖仍待 T002/T003 消除。

---

# Phase B：Cache 一级模块收敛

## [x] T002（P0）：将 Cache 提升为一级 Application 模块并重组 owner/DI

依赖：T001。

目标：消除历史上 `Application.Playback.Cache` / `Playback.ActiveCache` / `Playback.Export` 与现有 `Application.Cache` 并存的模块归属，使 Cache 的 namespace、registration 和 Infrastructure adapter 结构与实际职责一致。

实施方向：

1. 审计以下能力的真实 owner：
   - Cache identity/fingerprint；
   - `IAudioCache` / `IAudioCacheStore`；
   - CacheCatalog / Coverage / invalidation；
   - ChapterSpeechPlan / repair；
   - ActiveCache；
   - Cache-backed chapter export；
   - cache protection / maintenance / persistence adapters。
2. 以 `NovelSpeaker.Application.Cache` 为一级边界收敛 Cache-owned 类型，可按 `Plans/ActiveCache/Export` 等子目录组织。
3. 建立独立的 Cache Application registration，例如 `AddNovelSpeakerCacheApplication()`；`PlaybackRegistration` 不再顺带注册 Cache owner。
4. Infrastructure 中与 Cache 物理文件/index/maintenance/plan store/export writer 相关的实现按真实职责收敛，DI registration 与 Application 模块相对应。
5. ActiveCache/Export 若依赖当前位于 Playback 的**非 session 通用能力**，先判断其真实 owner：
   - 属于 TTS/audio generation 的，迁到 Speech/Cache 等合理边界或提取窄稳定 role port；
   - 属于 book content/query 的，复用 Books 稳定 contract；
   - 不允许为了完成目录搬迁形成 Cache ↔ Playback module cycle。
6. Playback session owner、PlaybackSnapshot、Prefetch session 生命周期保持不变。
7. 行为保持：cache key、文件布局、SQLite schema、export 输出、active cache 优先级均不得因 namespace 重构改变。

迁移清理（本任务必须完成）：

- 删除旧 `NovelSpeaker.Application.Playback.Cache/ActiveCache/Export` namespace 残留；
- 删除空目录、forwarding type、旧 using alias；
- 删除旧 Cache DI alias/重复 registration；
- 更新测试 namespace/fake，不保留兼容测试 adapter；
- 不新增 SQLite migration；
- ArchitectureTests 中属于 T002 的临时 debt baseline 全部删除。

测试：

- Cache Application/Infrastructure focused tests；
- Playback/Player/BookDetails/CacheManagement 关键集成与 Presentation tests；
- ActiveCache/Export lifecycle tests；
- DI tests；
- ArchitectureTests。

完成成果：Cache 的 Application/Infrastructure 类型、ActiveCache、ChapterSpeechPlan、Export、audio generation 与物理存储已迁入一级 Cache 边界；新增 Cache Application/Infrastructure registration，Playback 仅保留 session owner，并通过 Books contract、Speech rule/preview role 消除旧反向 owner。删除旧 `Application.Playback.Cache/ActiveCache/Export` 路径、alias 和重复 registration；更新测试与 Architecture baseline。验证：Application build、Unit 167/167、Infrastructure Integration 342/342、Architecture 46/46、WPF DI/navigation 18/18；静态复审 PASS。WSL 测试宿主启动需要沙箱外执行，未改变测试结果。

---

# Phase C：配置源与 Cache 解耦

## [x] T003（P0）：用 typed source change 消除 Settings/Rules → Cache 反向依赖

依赖：T002。

目标：Settings、TTS Rules、Regex/Text Processing 只负责自身状态和 mutation 语义；由 Cache-owned integration 判断这些变化是否导致 Coverage/Plan 失效。

当前重点债务包括但不限于：

- `AppSettingsService` 直接依赖 `ICacheInvalidationCoordinator`；
- TTS rule mutation/use case 直接发布 Cache Coverage invalidation；
- Regex replacement rule workspace 直接发布 Cache Coverage invalidation。

实施方向：

1. 先列出真正影响 Cache identity/plan/Coverage 的源变化：
   - Selected TTS rule；
   - default speak speed；
   - read chapter title；
   - text segmentation options；
   - Regex rule semantic changes；
   - TTS rule semantic changes。
2. 优先复用已有 `IAppSettingsService.Changed` 等稳定 typed change source；只有现有合同无法表达语义时才增加最小 typed change contract。
3. Settings/Books/Speech mutation 成功提交后只发布自身变化，不引用 Cache namespace。
4. 在 Cache 模块建立唯一的 configuration-change integration/observer，把源变化映射为 Cache Coverage/Plan invalidation。
5. observer 为 process owner，启动/关闭和订阅释放必须明确；不得依赖页面存在。
6. 不把 typed source changes 扩展成通用 EventBus，不建立“所有模块事件中心”。
7. 保持失效语义：配置变化只使 projection 失效，不立即全库重算 Coverage。

迁移清理（本任务必须完成）：

- 从 Settings/Books/Speech 删除所有 Cache-specific using、字段、构造参数和 DI wiring；
- 删除仅为旧反向 invalidation 存在的 helper/interface；
- 删除重复事件或 adapter；
- 删除 T003 对应 Architecture debt baseline；
- 更新 focused tests，使源模块测试不需要 Cache fake。

完成成果：Settings 保留自身 `Changed` source，TTS Rule 与 Regex workspace 增加最小 typed change event；Cache 新增进程级 configuration observer，集中判断 Coverage 影响并由 Cache coordinator 管理订阅生命周期。已删除 Settings/Books/Speech 的 Cache invalidation 字段、构造参数、helper、DI wiring 和全部 T003 Architecture debt；不做 eager Coverage recomputation。验证：Application build、Unit 171/171、Infrastructure Integration 342/342、Architecture 46/46、WPF composition/startup 11/11；新增 observer 生命周期、有效 Speech profile、Display-only/disabled/no-op 映射测试，WSL 测试宿主仍需沙箱外启动。

验收：

- Books/Speech/Settings → Cache-specific API 的 ArchitectureTests 为零例外；
- Application module graph 无 cycle；
- 配置变更后 Cache active UI 仍能通过 invalidation 自动追上；
- 不出现全库 eager Coverage recomputation。

---

# Phase D：有限职责收窄

## [x] T004（P1）：只对明确过宽的 orchestration 做局部收敛

依赖：T003。

目标：在模块边界稳定后，仅处理已经有明显独立变化原因/生命周期的局部 orchestration；不再开启全面 ViewModel/Coordinator 重构。

优先审计：

1. `CacheManagementViewModel`
   - catalog reconciliation；
   - live invalidation/single-flight refresh；
   - physical/Coverage decoration；
   - selection；
   - export interaction。
2. `PlaybackCoordinator`
   - session truth/command orchestration 必须保留；
   - volume persistence/debounce 等明显不属于 session truth 的职责可评估抽离。

执行规则：

- 先以“是否有独立生命周期/变化原因/测试边界”判断，不能以文件大小或构造参数数量判断。
- CacheManagement 如确有必要，优先抽取 Feature-local concrete controller；不建立新的 Application façade 或 interface hierarchy。
- Playback 只允许抽离不拥有 Book/Chapter/Segment/epoch/current snapshot truth 的职责。
- 如果审计后认为某项拆分收益不足，可以在完成成果中记录“不拆”的证据，不强行改代码。
- 不调整用户交互和视觉设计。

迁移清理：

- 抽取后删除原类中的重复字段/helper/state；
- 不保留 forwarding method 兼容旧测试；
- 重写绑定旧私有实现的测试；
- 删除因此失去调用方的 Shared helper/DI registration。

验收：

- state owner 数量不增加；
- 页面/Playback 生命周期语义不变；
- ArchitectureTests 和相关 focused tests 通过。

完成成果：审计 `CacheManagementViewModel` 后仅将具有独立导出准备、目录选择、取消、进程导出状态订阅和 UI 投影生命周期的 orchestration 收敛到 Feature-local `CacheManagementExportController`；目录、选择、缓存 decoration 与 live refresh 仍由页面 ViewModel 统一拥有。`PlaybackCoordinator` 未拆分，保留 session truth/command orchestration，未发现可在不转移播放状态 owner 的前提下足够独立的局部职责。验证：Release build、CacheManagementViewModelTests 3/3、CacheManagementPageLifecycleTests 2/2、ArchitectureTests/ArchitectureRuleContractTests 46/46；静态复审 PASS。WSL 测试宿主启动需要沙箱外执行，未改变测试结果。

---

# Phase E：遗留清理与最终收口

## [ ] T005（P0）：清除本轮迁移遗留并执行完整质量门禁

依赖：T004。

目标：确保本轮结束后只有一套 Application 模块架构，为下一轮日志、性能遥测和诊断系统建设提供稳定接入面。

全项目审计并清理：

- 旧 `Playback.Cache/ActiveCache/Export` namespace/reference；
- 旧 Cache registration/alias；
- Settings/Books/Speech → Cache-specific reverse dependency；
- compatibility wrapper / forwarding type / Obsolete bridge；
- duplicate DTO/read model/controller/projector；
- orphan DI registration；
- empty directory / legacy namespace；
- 仅为迁移存在的 test fake/stub/helper；
- Architecture temporary debt baseline；
- TODO/临时 instrumentation/trace/dump/一次性脚本；
- 无真实调用方的 Shared helper。

必须确认：

- Application module dependency graph 无 cycle、无临时白名单；
- Cache 与 Playback registration/owner 已分离；
- Playback mutable session state owner 仍唯一；
- Settings process snapshot owner 仍唯一；
- Cache physical truth、Coverage、repair、ActiveCache、Export owner 清晰；
- 用户数据/SQLite migration/文件布局未因内部模块迁移改变；
- 日志、性能遥测、诊断系统没有在本轮被实现或预埋第二套基础设施。

最终门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

完成成果应简要记录：

- 最终 Application 模块图和允许依赖方向；
- 迁移/删除的主要旧 namespace 与 abstraction；
- 是否对 CacheManagement/Playback 做了有限拆分以及理由；
- 完整测试结果；
- 适合下一轮日志/性能遥测/诊断系统接入的稳定边界；
- 仍存在但不阻塞下一轮的风险。
