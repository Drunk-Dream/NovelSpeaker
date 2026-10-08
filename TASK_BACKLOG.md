# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：核心状态、生命周期与工作流收敛

本轮规划基线更新为 `dev` 的 `a7639f1f8209a8222c9784fbea75a8e6a315919b`。T001–T007 已完成并恢复完整门禁；以下规划在该提交的 Playback 重构结果上继续推进。

本轮只处理五类已经由源码证明的结构性问题：

1. 跨系统业务变更的后果仍由 UI 调用者传播；
2. Playback 已收敛为单一 runtime，但段落/章节 target 提交仍被音频准备阻塞，导致 UI 响应依赖网络/缓存时延；
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

本 Backlog 明确授权四个 **staged breaking migration window**：T001–T003、T004–T007、T008–T010、T011–T014。窗口内的任务可以在 task spec 列出的中间破坏态结束，不为临时可编译而保留旧/新双轨、转发 wrapper 或兼容构造器；各窗口的 Closure 任务必须恢复标准完整门禁。T015–T019 默认保持每个任务结束时可构建、可测试。

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

### staged breaking migration window：T004–T007（已结束，完整门禁恢复）

## [x] T004（P0）：建立 Playback authoritative runtime 与 transition 模型

依赖：T003。

目标：建立唯一高层可变 Playback runtime/session state，明确 command、transition、effect 和 snapshot projection 边界；本切片允许现有 Coordinator 调用方暂时未迁移。

完成成果：以 PlaybackRuntime 替代 PlaybackSessionState，唯一拥有完整 immutable runtime read、session identity/Source context、Book/content、逻辑位置、Provider/config、语速、resume/checkpoint、失败窗口、高层状态/message/cache/retry 与 session cancellation/audio protection。准备不改变当前状态或分配 session 资源，提交重验合法位置、runtime/revision 和取消状态后一次替换；旧 session 资源转交 retire effect，checkpoint、当前段 audio 与 prefetch 使用带完整身份的明确 intent。Audio result 集中校验 session/Book/Source/catalog/position，纯 SnapshotProjector 仅接受完整 runtime read 与 process volume；ProgressController 只持久化 immutable checkpoint，不再同步 low-level snapshot 或修改 session。删除旧 session 类、Coordinator 的平行状态字段/Book/Provider alias 与自由拼装的 SnapshotProjectionInput，不保留兼容 wrapper。新增核心 transition tests，保留并迁移 checkpoint token/失败/取消测试，将旧 session 资源测试并入 runtime 生命周期覆盖；合法/非法位置、commit 前拒绝/取消、旧 preparation/result、projection 纯度、checkpoint、失败窗口成功清零和资源释放覆盖，Playback focused unit tests 67/67、隔离 Application Release build、生产/测试 format 与 diff 检查通过。

中间态与后续迁移：T005 接续 Start/OpenPaused、Pause/Resume/Stop/Clear、jump/move/retry/skip、Provider/speed、Books/Regex 已提交变化、StartNewSessionAsync/字段级 rollback、checkpoint/prefetch 与 shutdown；T006 接续 local snapshot/completed/failed、ProcessEventCommandAsync、PublishSnapshot/BuildSnapshot、EventEpoch/重复身份判断、AudioController 代理职责、volume/stop-timer 的发布整合。以上旧调用仍引用已删除的 PlaybackSessionState/字段/旧 ProgressController 与 projector API，未经隔离的 Application build 因 Coordinator 的 PlaybackSessionState 引用报 CS0246，属于本窗口允许的中间破坏态；完整 solution 门禁由 T007 恢复。focused 验证使用仓库外一次性副本，仅排除 Coordinator、PlaybackRegistration 及依赖它的 Application DI composition 文件，不排除 Playback 单元测试，不新增持久化变更、产品语义或长期文档解释；无环境限制。

## [x] T005（P0）：迁移 Playback 命令与 session replacement 生命周期

依赖：T004。

目标：将 start/open/jump/move/provider/speed/stop/clear、失败回滚和 checkpoint 迁入统一 transition/commit 语义，删除平行字段和隐式提交路径。

完成成果：Coordinator 的用户命令及 Books/Regex/Provider/Settings 已提交变化进入既有串行边界，以 PlaybackRuntime 唯一拥有 Book/Source、位置、配置、恢复位置、失败窗口和高层状态。目标内容与合成准备不停止旧音频、不保存目标或释放旧文件保护；验证合法目标与来源后一次 commit，随后退役旧 session/prefetch、保存 immutable checkpoint 并执行音频/预取 effects。准备失败、取消及忽略取消的迟到内容保留旧会话；commit 后 checkpoint 失败保留目标并允许恢复，不重拼旧字段。stop/clear/shutdown 保持稳定保存边界，metadata-only 不打断音频，Provider 与语速变化作用于下一句，Regex 按来源位置重新映射；连续跳过 3 段后暂停及显式恢复清零窗口由 runtime transition 维护。删除 StartNewSessionAsync、字段级 rollback、旧 session/Book/Provider/snapshot alias、自由拼装 snapshot 和 SegmentRunner 的合成播放一体入口；音频回调接入 runtime 的最小桥接恢复编译，AudioController/epoch 与低层回调最终收敛仍归 T006。保留并迁移 checkpoint、失败恢复、Source/Regex 与流水线核心测试，新增内容准备失败/取消、打开期间 removal/metadata、旧音频保护、Settings/Provider 提交与下一句、合成/checkpoint 等待期间的配置重验、Stop 后服务选择/清空与 Regex 刷新/取消、Regex 重映射保留音频回调、导航内容读取及 Settings/Provider 事实预取取消、跨句定时停止、捕获旧身份取消、结束 checkpoint 失败退役、损坏音频重生成取消和空内容异常覆盖，更新架构测试旧类型引用；Playback unit 68/68、相关 Integration 140/140、Architecture 26/26、Application Release build（0 warning/error）、format 与 diff 检查通过。完整 solution 门禁留给 T007；无持久化变化、一次性仓库产物、环境限制或长期文档冲突。

## [x] T006（P0）：收敛 Audio effect、异步回调与 Snapshot 投影

依赖：T005。

目标：让低层音频只作为带稳定 session identity 的 effect/result 输入，Snapshot 由 authoritative runtime 纯投影；删除重复 audio state、冗余 epoch 判断及无价值代理层。

完成成果：低层 completed/failed/snapshot 结果携带源快照、Playback session ID 与本地 AudioGeneration；Playback runtime 仅接纳当前 session 和当前音频 generation 的结果，保留 Regex 重映射期间有效音频回调。移除高层 EventEpoch 和无独立职责的 PlaybackAudioController，Coordinator/SegmentRunner 直接消费 local audio port。新增迟到旧 session 三类回调及完成回调身份覆盖；Application focused tests 47/47、PlaybackCoordinator integration 124/124、Release solution build（0 warning/error）、solution format verify 与 diff 检查通过。无持久化变化。

## [x] T007（P0）：清理 Playback 旧状态体系并完成 Phase B 验收

依赖：T006。

目标：删除旧接口、alias、wrapper、重复状态与过渡测试，验证核心播放、失败恢复、Source/Regex 变化和迟到回调，结束本窗口并执行完整门禁。

完成成果：PlaybackRuntimeState 仍只由内部 PlaybackRuntime 持有和替换；PlaybackCoordinator 继续作为命令/effect façade，Snapshot 为 runtime 投影，local audio 保留设备快照与本地 generation。PlaybackEventCommand 删除重复 SessionId 副本，事件去重与接纳均从原始 audio snapshot 读取身份；删除针对旧 PlaybackSessionState mutator 名称的失效架构扫描和旧接口形状/迁移负断言，改为验证 runtime/state 非 public、Current 仅 private set，并保留各 Playback role 共享同一 coordinator 的 DI 验证。Provider production pipeline 测试改为等待稳定 Playing/Stopped，而非中间 Preparing，并验证 next-position checkpoint；保留 runtime commit、失败恢复、Source/Regex 与迟到回调核心行为覆盖。仓库搜索确认 T001–T006 旧 Playback 类型/API/状态字段已清除；locked restore、format verify、Release build（0 warning/error）、完整 tests 1157/1157（含 WPF isolated Desktop 100/100）及 diff 检查通过。无持久化变化、环境限制或长期文档冲突。

## 5. Phase B2：把 Playback 改为 target-driven 流程

### staged breaking migration window：T008–T010（已结束，完整门禁恢复）

## [x] T008（P0）：建立 Playback logical target 与 preparation 身份

依赖：T007。

目标：保留单一 authoritative runtime，但把 Playback session context、logical target / target revision、播放意图与音频 preparation 分开；同一 Book/Source 内的切段切章不再通过“音频准备完成后才 commit session replacement”表达。

完成成果：PlaybackRuntimeState 以带 Book/Source/session 身份和单调 TargetRevision 的 PlaybackLogicalTarget 作为逻辑位置 owner，Snapshot 与 checkpoint 从其投影；同一 session 的 target commit 无需音频 ready，即清除旧 target 音频事实、产生新 logical checkpoint，并输出停止旧音频、取消过期 preparation、准备新 target 与刷新 prefetch 的 effects。播放意图、preparation kind、含唯一 attempt ID 的 session/target/synthesis identity 独立建模；local audio request/snapshot 携带 target revision 与 attempt，音频结果、设备回调和 checkpoint 只接纳当前 target/preparation，旧 attempt、旧 target 与配置变更前的 preparation 被拒绝，transport Stopped 不覆盖用户 Play 意图。新增 runtime 与本地音频身份测试，PlaybackRuntime/LocalAudio focused tests 49/49、Infrastructure IntegrationTests Release build（0 warning/error）、solution format verify 和 diff 检查通过。实际导航与自动推进调用链迁移及完整门禁留给 T009/T010；无持久化变化或长期文档冲突。

## [x] T009（P0）：迁移导航、自动推进与音频准备流水线

依赖：T008。

目标：显式切段/切章在必要的逻辑目标解析完成后立即 commit target 并发布 Snapshot，随后停止旧音频、异步准备新音频；音频失败或取消不回退用户已经提交的 target，迟到结果通过 session + target/preparation identity 拒绝。

完成成果：Start、同 Book/Source 导航、自动下一段、失败跳过/重试与 Regex 位置重映射已切换为逻辑 target 先提交、发布快照后异步准备音频；普通导航保留 session，Book/Source 变化仍走窄 session replacement。Preparation 取消与结果提交生命周期分离，避免失败后切换 target 时误取消新目标的 checkpoint；停止态 Regex 更新保留最新内容并在 Resume 时建立新 session。Provider/speed 在启动前复核；target 切换以 revision 替换旧预取，仅保留新 current target 的可复用 active key，目标为空时提交空窗；checkpoint 失败时不保留旧预取，Source 失效取消目标 checkpoint；Pause/Resume 和异步准备生命周期保持一致。新增语速 checkpoint 竞态、Pause→Resume→Navigate、关闭期间取消 preparation、active prefetch 保留、全 Regex 删除内容清空预取窗、Source 失效取消 checkpoint 回归覆盖。Playback Application focused tests 78/78、Infrastructure Integration playback focused tests 150/150 通过；无持久化变化或长期文档冲突。完整门禁留给 T010。

## [x] T010（P0）：收敛 Playback 等待体验并完成 Phase B2 验收

依赖：T009。

目标：统一 Preparing/Recovering/Playing/Paused 等用户语义，消除只用于瞬时本地交接的伪 Buffering；Player 对真实可感知的音频等待延迟显示“正在准备音频”，短等待不闪烁，并完成 target-driven Playback 的测试与旧 replacement 路径清理。

完成成果：删除 Playback 高层 `Buffering` 状态并保留其它 enum 数值；新 target commit 和当前 target 的准备/重试均投影为 Preparing，损坏音频恢复显示明确的重新生成语义。PlaybackSnapshot 投影已提交 TargetRevision，Player 在页面 activation 内延迟显示紧凑音频准备提示，短等待不显示、播放/暂停/失败/停止或换 target 后及时隐藏；可控 TimeProvider 测试覆盖延迟、快速跳转重置、页面离开与恢复语义。Preparing/Recovering 的 Player、MiniPlayer、桌面切换和 SMTC 操作映射到暂停意图；active preparation 上重复 Resume 不重启合成。普通同 Book/Source 导航及自动推进继续先 CommitTarget，`PrepareReplacement/CommitReplacement` 仅保留 Book/Source session replacement。迁移集成测试等待稳定播放状态并验证最终 Regex target；新增 Player 延迟反馈、target revision、暂停命令、SMTC 映射与准备期间重复 Resume 覆盖。Phase B2 搜索确认无 Buffering 引用且普通 target flow 不调用 replacement；locked restore、format verify、Release build（0 warning/error）、完整 tests 1181/1181（含 isolated Desktop WPF 101/101）与 diff 检查通过。无持久化变化、环境限制或长期文档冲突。

## [x] T010A（P1）：Playback 架构收口与减法

依赖：T010。

目标：在不改变 target-driven 用户行为、不重新打开并发架构的前提下，清理 T008–T010 迁移后的重复 effect 表达、session replacement 命名和无价值残留；审计 `PlaybackCoordinator` 职责，只在存在明确独立 owner 时做小型提取，并以净删除、边界更清楚和完整门禁通过作为完成标准。

完成成果：删除 Runtime 从未执行的 `PlaybackStopTargetAudioEffect` / `PlaybackRefreshPrefetchEffect` 及全部产生路径、测试断言；target 停止仍由 Coordinator 根据当前设备身份在 target commit 前执行，prefetch 刷新仍由 Coordinator 显式调度。剩余 prepare/cancel/checkpoint/session-retire effects 分别由唯一 Coordinator helper 执行。将 session 候选、准备/提交方法、拒绝值和 Coordinator helper 改为明确的 session replacement 命名；该路径只创建新 session（Book/ActiveSource context 改变或当前 session 不存在），同 session 导航、自动 next、retry/skip 继续走 target transition。PlaybackCoordinator 保留命令 façade、已提交变化消费、session/target 编排、音频准备、prefetch、设备回调、ReadingProgress、stop timer、volume persistence 与内容/位置解析职责；未提取职责，因为它们共享 serialized command/process 生命周期，且已有 Runtime、local audio、segment runner、position resolver owner，单独搬移不会移除 Coordinator 的 state/orchestration。Production code 净删除 effect 类型及重复表达，无新增抽象；保留既有 Playback 行为测试，仅更新 session replacement 内部命名并删除过期 effect 形状断言。locked restore、format verify、Release build（0 warning/error）、完整 tests 1181/1181 与 diff 检查通过；无持久化变化、环境限制或长期文档冲突。未发现本任务范围外仍需立即处理的问题；serialized local audio start 与 checkpoint/preparation 时序保持原样。

## 6. Phase C：把 Cache 一致性收回 Cache 模块

### staged breaking migration window：T011–T014（已结束，完整门禁恢复）

## [x] T011（P0）：建立 Cache-owned read model 与内部 repair 流水线

依赖：T010A。

目标：Cache 内部组合 physical facts、catalog、coverage、configuration invalidation 与 speech-plan repair，对 App 暴露已组合的场景化 read model 和窄变化通知。

完成成果：新增 Cache-owned overview、book list/catalog 与稀疏 chapter window read model，组合 physical facts、current-configuration coverage 和 export availability；提交时增加内存 revision，查询在 Cache 内拒绝混合 revision，合并通知仅暴露 Global/Book/Chapters 与 revision。missing/stale plan 在组合查询内登记 process repair，实际补建在后台运行并保持 in-flight dedupe、并发限制与 shutdown；完成后发布最窄章节变化。Cache integration 接收 Books committed changes 与 cache limit/configuration changes。新增组合状态、查询竞态、repair 去重/完成通知、observer isolation/drain 与 Books/limit 变更核心回归，保留原 Cache 持久化测试；旧页面接口按窗口合同留至 T012–T014。Cache Application focused tests、Infrastructure focused tests、架构检查与 format verify 通过；无持久化变化或长期文档冲突，完整门禁留给 T014。

## [x] T012（P0）：迁移 CacheManagement 到 Cache read model

依赖：T011。

目标：删除 CacheManagement 对 PhysicalSummary/CatalogStructure/Coverage、repair request、epoch/generation 刷新算法的理解，只保留页面选择、窗口和投影。

完成成果：CacheManagement 只消费 `ICacheReadModel` 的 book/catalog/sparse chapter views 与 Global/Book/Chapters 通知；删除旧 invalidation aspect 解释、repair requestor、多 query 拼装、pending dirty 集合、book epoch/generation 和 stale requeue worker。保留页面 activation/selected-book/latest-window identity、Extended Selection、增量 collection delta 与稀疏 decoration；行使用 read-model revision 拒绝迟到覆盖，导出可用性直接投影 Cache 结果。普通章节变化只查询明确 affected indices，目录增删后台计算 delta，保留有效选择且不全量 Clear/Add；书籍列表增删/重排映射在后台构建并与 UI 原子应用。保留并迁移原加载/清理/大目录与真实 WPF 生命周期核心测试，新增窗口/0%、目录增删、selected-book 清空、无关书籍、book-scope、书籍重排及迟到结果回归；全局通知在后台计算书籍差异，10,000 本未变化书籍保持 row identity，较大真实变更复用 staged projection 并保留选择；重排期间复用 loading 交互门并拒绝迟到点击，受控暂停回归验证无重复/遗漏 BookId。后台投影期间换书时以实际投影选择标记清除已删除书籍，并在排队章节加载获得投影门后重建窗口请求，受控并发回归验证无残留选择或占位行。CacheManagement presentation 16/16、WPF 页面/DI focused 8/8、架构 26/26、Release App build 与 format verify 通过；并行构建曾出现 CS2012 文件锁冲突，串行重试通过，无遗留环境限制。无持久化变化或长期文档冲突；其它页面与旧接口按 T013–T014 继续收口，完整门禁留给 T014。

## [x] T013（P1）：迁移 Player、BookDetails 与 CacheAndData 的缓存投影

依赖：T012。

目标：其它页面消费相同 Cache read model/change source，不再直接订阅 Cache 内部 invalidation aspect 或自行驱动 coverage/repair。

完成成果：Player、BookDetails 和 CacheAndData 全部消费 `ICacheReadModel`；页面不再订阅 invalidation aspect 或根据配置变化自行驱动 coverage，Cache-owned 通知明确标识 overview 是否变化，纯 coverage 通知不重查物理总览。删除旧 `ChapterCacheStatusRefreshController`，保留只负责 activation 内稀疏窗口查询合并的 presentation slot；Player 在 catalog 切换时取消旧查询，BookDetails 按 activation/book 身份拒绝迟到结果。整书/全局通知保留 viewport 并补查 current，章节通知只查明确 affected indices；Active Cache snapshot 与 management selection 仍由原 owner 负责。保留并迁移原页面与 WPF 核心测试，新增/扩充大目录窗口、无关书籍、overview scope、目录 activation 与退出迟到结果回归。修复真实 WPF 测试宿主意外运行生产 App startup 的既有问题：从生产 App.xaml 按原顺序加载实际资源与输入桥接，不启动 shell/tray、不放宽 Desktop 隔离；原基线单测也复现超时，转储定位到生产托盘失败后的 modal MessageBox，修复后 WPF focused 20/20 通过。Presentation/architecture focused 92/92、Cache Application focused 36/36、Release build（0 warning/error）、format verify 和 diff 检查通过；临时转储、工具与基线 worktree 已清理，无遗留环境限制、持久化变化或长期文档冲突。完整门禁留给 T014。

## [x] T014（P0）：删除 Cache 泄漏接口并完成 Phase C 验收

依赖：T013。

目标：删除 App-facing `ICachePlanRepairRequestor`、内部失效语义和重复刷新 controller，收敛测试，结束本窗口并执行完整门禁。

完成成果：删除旧 repair requestor/interface 和未使用的 invalidation 测试替身，移除旧 catalog 测试包装入口；catalog、coverage、invalidation batch/coordinator、repair request/coordinator 全部内收 Application。Infrastructure 仅通过物理提交 sink 发布事实，Bootstrap 仅依赖 repair/change 的窄 Stop 生命周期角色；各角色复用同一 process owner，不增加一致性状态。App 只消费 `ICacheReadModel` 场景化投影与窄变化，删除 CacheAndData mutation 前后比较刷新版本的旧一致性 helper；Player/BookDetails 只保留页面 activation、稀疏窗口与查询合并，CacheManagement 无 pending aspect/epoch/repair worker。新增 App Cache 边界架构检查，扩充原 shutdown/DI 测试验证 drain 顺序、导出超时后继续关闭和 owner 身份；保留 identity、atomic write、coverage、repair、scope、稀疏窗口/增量、background owner、Book/Source removal 与 lease 核心测试。全量验收首次暴露四个真实导航测试未初始化隔离数据库（SQLite no such table: Books）；改用现有异步初始化测试工厂后 WPF 102/102 通过，未放宽 Desktop 隔离或修改生产数据库。locked restore、format verify、Release build（0 warning/error）、完整 tests 1205/1205（含 architecture 与 isolated Desktop WPF）及 App 禁止类型搜索、diff 检查通过，Phase C 窗口结束。无持久化变化、遗留环境限制或长期文档冲突。

## 7. Phase D：统一页面异步生命周期

## [x] T015（P1）：补齐 activation 与 latest-wins 小型生命周期原语

依赖：T014。

目标：区分 page activation、latest-wins operation 与真正的业务 revision，扩展现有设施而不是建立异步框架；用代表性测试固定取消、迟到提交和释放语义。

完成成果：核对 Library 搜索/投影、BookDetails 加载、PlaybackSettings debounced save、Player content load 和 SpeechServices voice filtering；搜索、筛选与后发覆盖的页面查询共享 latest-operation 语义，播放/catalog/layout 与 voice editor session 身份保持独立。新增小型 `LatestOperationSlot`，只拥有 linked CTS、单调 operation identity、currentness/commit、异常观察和重复安全释放，直接链接既有 PageActivationScope；不引入业务 DTO、调度器或第二套 page lifetime。代表性迁移 Library 搜索 debounce，删除搜索 CTS/version 与手工 finally 释放，净删 34 行页面样板。保留原 activation/Shared/Library 核心测试，新增两项合并生命周期回归，覆盖 replacement 不取消 activation、页面取消/解除订阅/drain、迟到结果/异常拒绝、current failure 和重复释放。Shared/activation/Library focused 30/30、format verify、Release build（0 warning/error）与 diff 检查通过；首次并行验证发生文件锁重试，串行重跑通过。无持久化变化、遗留环境限制或长期文档冲突。

## [x] T016（P1）：迁移 Library 与 BookDetails 的页面异步状态

依赖：T015。

目标：用 activation/operation owner 替换两页中重复的 CTS/version/OwnedTaskRegistry 样板；保留真正需要的目录、播放与布局 revision。

完成成果：Library load/import/search/projection/background row layout 与 BookDetails critical load/staged enrichment/cache observation 使用 activation-linked operation owner；Books/Playback/Cache 订阅注册到唯一 page activation，任务 completion/exception 直接由 activation 观察，缓存查询合并 slot 可附着 activation 观察自身工作，不并列保存同一批任务。删除两页 `OwnedTaskRegistry`、6 个裸 CTS 字段（含 Library management lifetime）、Library load/import/playback projection/row-layout version 与 BookDetails load/header/playback projection version；管理交互 session 使用独立 owner，退出管理取消其批量工作，普通单书导出仍属于页面。Library 保留并命名 `visibleProjectionRevision` 与 `playbackSnapshotRevision`，分别拒绝依据过期集合/索引及播放 decoration 构建的后台布局；BookDetails 保留 `cacheStatisticsRevision`，拒绝清理前统计覆盖清理后事实。WPF initial locator 身份、editor dirty/navigation guard、committed-change 串行门与 critical-load barrier 保持原职责；不取消 Playback/process/background owner。修正后台 catalog 索引完成后的取消检查，防止离开/切书后发布旧 catalog；迟到 load/import/statistics 异常不通知新页面。保留全部既有核心页面、10k、增量 row、播放/card、选择及 scroll 测试，新增四项受控生命周期回归验证连续搜索/快速返回、后发 load 覆盖迟到失败、跨 Book header/catalog/statistics/cache decoration、当前排序失败与被替换排序迟到异常，以及缓存失败重启查询的 activation drain。独立审查发现并修复重启查询漏挂 activation 和被替换 projection/layout 的迟到 Snackbar；初次与重启查询统一注册路径，layout 由 operation.RunAsync 观察，测试无固定等待。Presentation 全部 329/329（含 architecture）、两页/WPF navigation/DI focused 13/13、Release build（0 warning/error）、format verify 与 diff 检查通过。无持久化变化、遗留环境限制或长期文档冲突；Phase D 完整门禁按计划留给 T017。

## [x] T017（P1）：迁移其余高收益页面并完成生命周期验收

依赖：T016。

目标：迁移 Cache/Player/Settings/SpeechServices 中与共享模式等价的手写生命周期，删除不再需要的设施，执行完整门禁；不强行统一语义不同的操作。

完成成果：CacheManagement load/selection/decoration、CacheAndData cache-limit debounce/save、Player activation/book/chapter/content/cache decoration/speed debounce、PlaybackSettings/GeneralSettings/ImportTextSettings 保存，以及 SpeechServices provider refresh/voice editor/catalog/search/试听准备统一附着 activation 与 latest-operation owner。订阅和页面任务由 activation 释放、观察并 drain；离页与 replacement 的迟到结果/失败不提交 UI 或 Snackbar。保留 Playback session、Active Cache、已提交 Export、Speech Plan repair 与 Provider preview audio 独立 owner，试听音频仍由显式停止操作管理。删除 9 个重复 OwnedTaskRegistry 字段、16 个裸 CTS 字段、20 个 page/query/save generation/version 字段及重复保存基线，生产代码净删 564 行；共享 registry 保留给真正非 activation owner。保留既有核心测试，新增 13 项合并生命周期回归，覆盖 latest save、持久提交后取消再改回、清理离页、快速返回/切换、旧 decoration/catalog/search/试听失败、取消后重试及导入最终刷新取消后不发送完成通知。完整门禁首次发现播放集成测试把最后发起请求误当成实际播放内容，三项既有回归改为带标识的音频结果断言；WPF 居中回归等待实际目标居中，避免动画重排间隙误判。隔离宿主挂起 WPF IME，避免系统输入服务线程保留 Desktop；真实 Window/Focus/键盘路由、隔离与 fail-closed 清理继续验证，未启用可见窗口。locked restore、format verify、Release build（0 warning/error）、完整测试 1224/1224（Domain 15、Application 293、Infrastructure 472、Presentation 342 含 architecture、隔离 WPF 102）及 diff 检查通过。无持久化变化、遗留环境限制或长期文档冲突。

清理审计：等价 page/query/save CTS + generation/currentness 已迁移；Cache read-model row revision、overview 请求/应用 revision、Player catalog/content/position、stop-timer/preparation target、Library/BookDetails 数据投影及 WPF locator/layout/viewport/collection transaction revision 保留真实语义。Playback runtime/prefetch/低层 audio generation/音量保存、Active Cache/Export/repair/audio generation、HTTP/Edge 请求、logger、Bootstrap/Shell/Desktop/MiniPlayer、通知和 import-dialog 的 lifetime 属于非 page owner，保留其 CTS/registry。Rules 三个 workbench 的 management session 留给 T018，Speech editor orchestration 留给 T019；Appearance theme apply 与 Diagnostics 生效/恢复序号保留领域确认语义，不机械消除所有 version。无无人使用的 helper、临时 adapter 或诊断代码遗留；T017 临时规格已删除，Phase D 完成。

## 8. Phase E：编辑工作台做减法

## [x] T018（P2）：提取 Rules 编辑工作台的明确行为 owner

依赖：T017。

目标：围绕现有 `EditorSession`、`RuleImportSession`、`RuleReorderController`、`ManagementSelectionController` 收敛 Chapter/Regex/Metadata 的编辑、交换、排序和批量管理流程，不建立通用泛型 ViewModel。

完成成果：三类工作台组合现有 EditorSession 的 original/new/dirty 与保存/放弃/取消保护，Metadata 删除 original/isNew/HasEditor 平行 bookkeeping，保留空白新草稿即 dirty 和自身 typed read/write/remove/order、validation 与交换格式。ManagementSelectionController 拥有异步进入保护，页面直接连接既有 activation，删除三个管理 CTS 与各页 entering/deleting flags。小型 WorkbenchExchangeInteraction 拥有单文档构建、文件/剪贴板写入和取消检查；BatchDeleteSession 拥有单次确认后的 busy lifetime、逐项成功/跳过/失败继续处理与最终 reconciliation，不持有业务 repository、规则字段或反馈文案。Chapter 合并备用排序与 draft change 的重复路径，Metadata 使用稳定键 offset 计算并在排序失败后恢复投影；内建 Chapter 限制、Regex scope/timeout、normal/management selection 分离保持。三类 ViewModel 净删 123 行重复 orchestration，新增/扩展 owner 与 activation 接线后生产代码总量基本持平（+2 行）。保留既有核心测试，新增六项合并 owner 行为回归，覆盖失败保存不丢 dirty、迟到取消决定、管理进入串行、批量 partial continuation/取消重试及取消后迟到文档拒绝。Rules/Shared/selection/architecture focused 87/87、WPF navigation/DI 7/7、Release build（0 warning/error）、全仓 format verify、LF 与 diff 检查通过。无持久化变化、临时产物、环境限制或长期文档冲突；最终全量门禁留给 T019。

## [x] T019（P2）：收敛 Provider 工作台并完成本轮架构验收

依赖：T018。

目标：让 Provider 复用真正相同的工作台行为，同时保留 HTTP/Edge typed editor 差异；删除重复 orchestration，审计五类结构目标并执行完整门禁。

完成成果：EditorSession、WorkbenchImportSession、WorkbenchReorderController、WorkbenchExchangeInteraction 与 BatchDeleteSession 归入跨业务域 `Shared/Presentation/Workbenches`，删除 SpeechServices 对 Rules Feature 的依赖和旧命名/路径，不保留转发层。Provider 复用 dirty leave guard、管理进入保护、导入 busy/取消、单文档文件/剪贴板交换和批量删除 continuation；HTTP headers/body/rate 与 Edge Voice 仍分别使用 typed draft、validation/save mapping，凭据导出确认、Provider envelope、复制新身份、隐藏项完整排序、试听与 Voice Catalog/search 仍由 Provider owner 负责，CurrentProvider 只读取 Settings truth。保留现有核心测试，将 dirty selection theory 扩展到 Edge 的保存/放弃/取消（三个新用例），不复制共享 owner 的全部测试；没有需删除的旧 orchestration 形状断言或临时测试。T018–T019 四个工作台 ViewModel 合计净删 126 行；生产总代码基本持平（新增 owner/activation 接线抵消删减，合计 +1 行），收益是单一行为 owner；本轮较大减法包括已删除的 Playback 平行状态/audio 代理、Cache 泄漏接口/repair worker，以及 T015 的 34 行和 T017 的 564 行生命周期样板。

本轮五项架构验收：

- 变更传播：核对 Books metadata/import/removal 和 Regex mutation 的持久提交后 typed 发布与 observer 隔离，Playback/Cache/page 各自消费；旧 UI Books/Regex 后果编排与全局 invalidation state 已删除，无通用 EventBus/Messenger。
- Playback：唯一 PlaybackRuntime 持有 authoritative read；同 Book/Source 导航和自动推进 CommitTarget，Book/Source context 改变才 replacement；SnapshotProjector 为纯投影，audio callback 校验 session/target/preparation attempt 与低层 generation，无旧 runtime 或 audio forwarding wrapper。
- Cache：ICacheReadModel 内部组合 physical/catalog/coverage、跨 revision 重查与 process repair，对页面只发布 Global/Book/Chapters 展示变化；App 不读取 internal invalidation aspect、coverage query 或 repair protocol。
- 页面生命周期：页面接线统一使用 activation，latest-operation owner 独立；未发现残留等价 page CTS/version/task registry。保留 data/projection revision、播放/session/background owner、导出准备 admission 与导入进度 dialog task owner，不改变已提交后台工作生命周期。
- 工作台：Rules/Provider 组合小型 internal owner，Shared 不依赖 Feature 或业务 repository；没有通用泛型 ViewModel、业务 DTO/格式统一、字段字典或新循环。生产 interface 扫描未发现无引用声明；剩余 adapter 属于实际 Windows/日志技术边界，Legacy Provider codec/migration 仅用于已发布持久兼容。

自动验收：Provider/owner/architecture 首轮 focused 64/64，随后 locked restore、全仓 format verify、Release build（0 warning/error）、完整 tests 1233/1233（Domain 15、Application 293、Infrastructure 472、Presentation 351 含 architecture、isolated Desktop WPF 102）、旧 API/Shared→Feature 搜索、LF 与 git diff 检查全部通过，未启用可见窗口。无持久 schema/格式变化、临时产物、未执行的必要检查、环境限制或长期文档冲突；本轮所有 migration window 已结束。

## 9. 本轮明确不处理

- 不重新拆分 Domain/Application/Infrastructure/App 四个工程。
- 不因 `PlaybackCoordinator`、Diagnostics store 或 ViewModel 行数大而机械拆类。
- 不删除 Books operation journal/recovery，也不简化其跨 SQLite 与文件系统的一致性保证。
- 不把 Diagnostics/Telemetry 纳入本轮，除非其调用方因上述边界调整必须做最小适配。
- 不统一 Chapter/Regex/Metadata/Provider 的业务模型、持久格式或导入协议。
- 不新增 schema、migration、持久 revision 或兼容层；若实现证据显示必须改变持久边界，停止相应部分并请求用户决策。
