# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：核心状态、生命周期与工作流收敛

本轮规划基线为 `dev` 的 `f1696a5faefcdf078016f0765f87a5b62fe93911`。上一轮 T001–T009 已完成，成果保留在 Git 历史中；当前计划按用户要求清空后从 T001 重新编号。

本轮只处理五类已经由源码证明的结构性问题：

1. 跨系统业务变更的后果仍由 UI 调用者传播；
2. Playback 运行态存在多份可变副本和复杂的事件有效性判断；
3. Cache 内部失效、repair、coverage 与 read model 一致性泄漏到 App；
4. Page activation、latest-wins operation 与页面私有 CTS/version/generation 尚未收敛；
5. Rule/Provider 编辑工作台重复维护相同的编辑、交换、排序与批量管理流程。

不做普通 Bug 清理、按文件行数机械拆类、全局分层重写、通用 EventBus/CommandBus、万能泛型工作台，或为小概率未来需求增加抽象。本轮不改变 SQLite schema、持久化数据格式或用户外部 TXT。

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，并追加简短成果
- `[!]` 阻塞，仅用于必须由用户决定的新产品、架构、隐私或持久化边界

默认按编号串行执行。每项任务以对应 `tasks/Txxx_*.md` 为权威实施合同；完成后在此记录成果并删除 task spec。

本 Backlog 明确授权三个 **staged breaking migration window**：T001–T003、T004–T007、T008–T011。窗口内的任务可以在 task spec 列出的中间破坏态结束，不为临时可编译而保留旧/新双轨、转发 wrapper 或兼容构造器；各窗口的 Closure 任务必须恢复标准完整门禁。T012–T016 默认保持每个任务结束时可构建、可测试。

## 3. Phase A：让业务变更后果回到模块 owner

### staged breaking migration window：T001–T003（已结束，完整门禁恢复）

## [x] T001（P0）：建立 Books 已提交语义变更合同

目标：让 Books mutation owner 在持久变更成功后发布 Metadata、Catalog、ActiveSource、Removed 等窄语义事实；不再用 `BookSourceCatalogChanged` 或 App 层 bool 代替所有变化。

完成成果：Books 通过专属 `BookCommittedChange` 发布 MetadataCommitted、ActiveCatalogCommitted、ActiveSourceChanged、SourceRemoved、BookRemoved，仅携带稳定身份/提交事实，并逐 observer 隔离异常。元数据验证、串行 mutation 和通知归 Application 用例，SQLite adapter 只持久化；删除旧 CatalogChanged 合同及 removal 空目录表达，不改 schema、journal、rollback 或 pre-commit work stopper。新增元数据核心测试，扩展并保留导入/删除及 SQLite 回归，覆盖新书、活动/非活动 Source、最后 Source 删除、失败/取消/回滚与提交后 cleanup recovery；Application Books 84/84、Infrastructure Books 69/69、format 和 diff 检查通过。focused tests 使用仓库外一次性编译隔离，移除旧 Playback 订阅入口并排除旧消费者专用测试，隔离产物已清理。未经隔离的 Application build 在未迁移的 PlaybackCoordinator 因旧 BookSourceCatalogChanged 报 CS0246，属于本窗口明确允许的中间态；消费者迁移归 T002，完整门禁归 T003。无环境限制或长期文档冲突。

## [x] T002（P0）：迁移 Books 变更消费者并删除 UI 后果编排

依赖：T001。

目标：由 Playback 与 Books presentation 自行消费 Books 变化，删除 ViewModel 对 `HandleBookDeletedAsync`、`RefreshBookMetadataAsync` 和 `BookCatalogInvalidationState` 的编排及旧接口。

完成成果：Playback 自行消费 Books committed facts，元数据在串行边界内合并且保留当前音频，相关 Catalog/ActiveSource 变化取消旧 session/prefetch 并拒绝迟到结果；删除继续只由 removal use case 调用 work stopper。Library/BookDetails 通过现有 PageActivationScope 在活动期间订阅变化并查询受影响 Book，离开后解除订阅、取消刷新并拒绝旧 activation 结果，非活动页面再次进入正常加载；详情刷新保留未保存草稿。删除 IPlaybackBookCommands、旧 CatalogChanged 消费、全局 BookCatalogInvalidationState 及 UI 跨模块后果编排，更新 DI、架构与页面 fixture。保留 Books 导入/删除/rollback 核心测试，改写 Playback 元数据/删除测试为 committed mutation 或 removal owner 路径，增加来源相关性、停止后的残留投影、页面订阅、内部加载替换、忽略取消与离开后失败抑制覆盖；Application Books 84/84、Books/Playback Integration 149/149、相关 Presentation/Architecture 63/63、Release build、format 与 diff 检查通过。Regex 旧 port 留给 T003，完整门禁由 T003 收口；无持久化变化、环境限制或长期文档冲突。

## [x] T003（P0）：收敛 Regex 变更传播并完成 Phase A 验收

依赖：T002。

目标：Playback 直接消费 Regex workspace 的 `Changed/AffectsSpeechProfile`，删除页面中的 `RefreshRegexReplacementAsync` 调用和旧 refresher port；审计其它跨系统 mutation，结束本窗口并执行完整门禁。

完成成果：Playback process owner 订阅 Regex workspace 的已提交变化，仅将 AffectsSpeechProfile=true 的全局事实排入既有串行边界并更新消费时有效的 session，dispose 解除订阅；首次 Start/OpenPaused 或跨 Book 打开期间不丢提交，连续提交不随音频 session 替换丢失，失效来源取消迟到内容投影。Speech Plan 变化按来源位置映射最近可播放段，包括下一段语音相同的情况；Display/name-only 变化保留当前音频。Workspace 各 mutation 和配置恢复逐 observer 隔离异常，保存、启停、删除、排序、导入及批量删除不再由页面传播 Playback 后果。删除 IPlaybackRegexReplacementRefresher、公开 RefreshRegexReplacementAsync、DI 和旧 refresher fake/调用计数测试；保留编辑、取消、批量反馈及 Books 核心回归，新增真实 workspace mutation → pipeline → Playback 覆盖、打开期间提交、连续提交与来源失效保护。Phase A 搜索确认旧 Books/Regex 传播 API、CatalogChanged 类型、全局 invalidation state 和迁移 wrapper 均已删除；剩余页面刷新用于自身投影。修复完整门禁发现的 Speech runtime 集成 fixture 缺少 storage path resolver 注册。Playback focused 79/79、Regex/Cache observer 22/22、相关 Presentation 11/11；locked restore、format、Release build（0 warning/error）、完整 tests 1078/1078（含隔离 Desktop WPF 100/100）及 diff 检查通过，T001–T003 窗口结束。无持久化变化、环境限制或长期文档冲突。

## 4. Phase B：重建 Playback 单一运行态

### staged breaking migration window：T004–T007

## [ ] T004（P0）：建立 Playback authoritative runtime 与 transition 模型

依赖：T003。

目标：建立唯一高层可变 Playback runtime/session state，明确 command、transition、effect 和 snapshot projection 边界；本切片允许现有 Coordinator 调用方暂时未迁移。

## [ ] T005（P0）：迁移 Playback 命令与 session replacement 生命周期

依赖：T004。

目标：将 start/open/jump/move/provider/speed/stop/clear、失败回滚和 checkpoint 迁入统一 transition/commit 语义，删除平行字段和隐式提交路径。

## [ ] T006（P0）：收敛 Audio effect、异步回调与 Snapshot 投影

依赖：T005。

目标：让低层音频只作为带稳定 session identity 的 effect/result 输入，Snapshot 由 authoritative runtime 纯投影；删除重复 audio state、冗余 epoch 判断及无价值代理层。

## [ ] T007（P0）：清理 Playback 旧状态体系并完成 Phase B 验收

依赖：T006。

目标：删除旧接口、alias、wrapper、重复状态与过渡测试，验证核心播放、失败恢复、Source/Regex 变化和迟到回调，结束本窗口并执行完整门禁。

## 5. Phase C：把 Cache 一致性收回 Cache 模块

### staged breaking migration window：T008–T011

## [ ] T008（P0）：建立 Cache-owned read model 与内部 repair 流水线

依赖：T007。

目标：Cache 内部组合 physical facts、catalog、coverage、configuration invalidation 与 speech-plan repair，对 App 暴露已组合的场景化 read model 和窄变化通知。

## [ ] T009（P0）：迁移 CacheManagement 到 Cache read model

依赖：T008。

目标：删除 CacheManagement 对 PhysicalSummary/CatalogStructure/Coverage、repair request、epoch/generation 刷新算法的理解，只保留页面选择、窗口和投影。

## [ ] T010（P1）：迁移 Player、BookDetails 与 CacheAndData 的缓存投影

依赖：T009。

目标：其它页面消费相同 Cache read model/change source，不再直接订阅 Cache 内部 invalidation aspect 或自行驱动 coverage/repair。

## [ ] T011（P0）：删除 Cache 泄漏接口并完成 Phase C 验收

依赖：T010。

目标：删除 App-facing `ICachePlanRepairRequestor`、内部失效语义和重复刷新 controller，收敛测试，结束本窗口并执行完整门禁。

## 6. Phase D：统一页面异步生命周期

## [ ] T012（P1）：补齐 activation 与 latest-wins 小型生命周期原语

依赖：T011。

目标：区分 page activation、latest-wins operation 与真正的业务 revision，扩展现有设施而不是建立异步框架；用代表性测试固定取消、迟到提交和释放语义。

## [ ] T013（P1）：迁移 Library 与 BookDetails 的页面异步状态

依赖：T012。

目标：用 activation/operation owner 替换两页中重复的 CTS/version/OwnedTaskRegistry 样板；保留真正需要的目录、播放与布局 revision。

## [ ] T014（P1）：迁移其余高收益页面并完成生命周期验收

依赖：T013。

目标：迁移 Cache/Player/Settings/SpeechServices 中与共享模式等价的手写生命周期，删除不再需要的设施，执行完整门禁；不强行统一语义不同的操作。

## 7. Phase E：编辑工作台做减法

## [ ] T015（P2）：提取 Rules 编辑工作台的明确行为 owner

依赖：T014。

目标：围绕现有 `EditorSession`、`RuleImportSession`、`RuleReorderController`、`ManagementSelectionController` 收敛 Chapter/Regex/Metadata 的编辑、交换、排序和批量管理流程，不建立通用泛型 ViewModel。

## [ ] T016（P2）：收敛 Provider 工作台并完成本轮架构验收

依赖：T015。

目标：让 Provider 复用真正相同的工作台行为，同时保留 HTTP/Edge typed editor 差异；删除重复 orchestration，审计五项结构目标并执行完整门禁。

## 8. 本轮明确不处理

- 不重新拆分 Domain/Application/Infrastructure/App 四个工程。
- 不因 `PlaybackCoordinator`、Diagnostics store 或 ViewModel 行数大而机械拆类。
- 不删除 Books operation journal/recovery，也不简化其跨 SQLite 与文件系统的一致性保证。
- 不把 Diagnostics/Telemetry 纳入本轮，除非其调用方因上述边界调整必须做最小适配。
- 不统一 Chapter/Regex/Metadata/Provider 的业务模型、持久格式或导入协议。
- 不新增 schema、migration、持久 revision 或兼容层；若实现证据显示必须改变持久边界，停止相应部分并请求用户决策。
