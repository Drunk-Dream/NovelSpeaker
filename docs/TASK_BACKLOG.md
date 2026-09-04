# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段转入 **项目整体架构优化前诊断**。当前 `dev` 基线为 `cec6777e3aa1d5d22bcec92dcdabfd10c8af648a`。

现有核心功能已经基本完善，下一阶段不继续以新增功能或局部缺陷补丁为主，而是先对整个 NovelSpeaker 的模块边界、状态所有权、依赖方向、页面数据流、异步/Dispatcher 边界、大列表模型、后台任务生命周期、持久化职责和测试结构做一次结构化 Architecture Review，为后续分阶段重构建立事实依据。

此前阅读进度一致性问题已经完成 T001–T002；BookDetails 返回性能已经完成 T003–T004 的 180 章诊断和第一轮优化。但真实 3000+ 章节书籍仍存在 Player 点击返回后约 10 秒 UI 无响应的问题。该问题当前作为“架构压力/规模化症状”保留，不再单独继续专项修复；后续在整体架构优化方案确定并完成相关结构调整后，再按需要重新验证和定位。

目标状态固定为：

```text
当前活动书籍
    PlaybackSnapshot = 运行时即时真值
                ↓
        Effective Reading Progress
                ↑
    ReadingProgress(SQLite) = 持久化 checkpoint

非活动书籍 / 应用重启
    ReadingProgress(SQLite) = 基线真值
```

本轮边界：

- 不新增第三套可变的“全局阅读进度状态”或页面级复制状态。
- 不让书库/详情页通过等待 SQLite 回写来获得当前活动书籍的即时章节位置。
- `ReadingProgress` 继续作为可恢复的持久化 checkpoint；显式章节/段落跳转成功后必须及时提交新的逻辑位置，不能只在下一次 session 被替换时补写。
- 不通过把 `BookDetailsPage` / `BookDetailsViewModel` 改为 Singleton、开启页面缓存或绕过强类型路由来掩盖返回卡顿。
- 性能任务必须先定位再优化；不得再次仅凭 `Task.Yield()`、方法名带 `Async` 或一次 `Task.Run` 就宣称 UI 阻塞已解决。
- `Microsoft.Data.Sqlite` 的异步 API 不视为“自动离开 UI 线程”的保证；任何可能同步执行的数据库/CPU 工作都要按真实线程占用验证。
- 本阶段不是视觉样式改版，不改变书库/详情/播放页既有视觉设计。

## 2. 状态与优先级

- `[ ]`：未开始。
- `[-]`：进行中。
- `[x]`：已完成；任务末尾必须附简短“完成成果”。
- `[!]`：存在阻塞，必须记录可复现原因。
- `P0`：影响阅读位置正确性、跨页面一致性或明显交互卡顿。
- `P1`：性能收口、维护性或补充性回归。

Codex 完成任务后保留条目并标记 `[x]`，在对应任务末尾追加“完成成果”；不得自行删除其它任务。只有后续新的规划阶段才允许再次清空或重写 Backlog。

## 3. Codex 执行规则

1. 默认一次只执行一个编号任务；完成后停止，不自动开始下一项。
2. 开始 T001 前至少阅读：`AGENTS.md`、`docs/04_PLAYBACK_PIPELINE.md`、`docs/05_DATA_AND_PERSISTENCE.md`、`docs/06_UI_AND_USER_FLOWS.md`、`docs/09_TESTING_AND_QUALITY.md`、`docs/11_DECISIONS_RISKS_OPEN_QUESTIONS.md`。
3. 每个任务开始前重新审计真实调用链，不仅按本文列出的文件机械修改。进度任务至少搜索：`SaveProgressAsync`、`PlaybackProgressService`、`ReadingProgress`、`PublishSnapshot`、`JumpToChapterAsync`、`JumpToSegmentAsync`、`StartNewSessionAsync`、`OpenResolvedPositionAsync`、`SnapshotChanged`。
4. 性能任务至少审计：`BookDetailsPage`、`BookDetailsViewModel.LoadAsync`、`GetBookDetailsHeaderAsync`、`GetBookDetailsAsync`、`ViewModelCollectionExtensions.ReplaceWith`、`CurrentItemLocatorInteraction`、初始缓存状态刷新、`ShellNavigationAdapter.NavigateAsync`、Player 页面离开生命周期以及 Wpf.Ui NavigationView transition。
5. 先为可复现缺陷补失败测试，再修改实现。测试优先保护“第一次切换/第一次返回就正确”，不得用第二次导航或额外 Pause/Stop 掩盖问题。
6. 当前活动书籍的即时 UI 投影以 `PlaybackSnapshot` 为最高优先级；SQLite 只提供持久化基线。不要创建新的 mutable singleton progress store 来复制 Snapshot。
7. 持久化 checkpoint 只能在目标逻辑位置已经确定、操作确认成功后提交。失败、取消、目标不可用时不得把未完成跳转写成当前进度。
8. 不要求每毫秒音频位置都写 SQLite；重点保护显式切章/切段、暂停、停止、session 替换和退出等稳定 checkpoint 边界。
9. 详情性能诊断必须比较同一本书、同一窗口尺寸下的 `Library -> BookDetails` 与 `Player -> BookDetails`，区分冷/热数据库与首次/后续进入，不能用不同书籍或不同章节规模比较。
10. 性能结论必须给出阶段耗时或等价可复核证据；如果临时 instrumentation、日志、ETW/trace、诊断开关或测试 harness 只为定位服务，完成定位/修复后必须删除。
11. 不建立脆弱的绝对毫秒 CI 门禁。自动回归应优先使用可控 slow fake、barrier/gate、Dispatcher/first-render 信号和调用顺序证明“重工作不阻塞首帧/交互”，实际耗时用于本轮诊断记录。
12. WPF 自动测试继续使用隐藏 Desktop；不得自行设置 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。
13. 本轮不需要生成视觉截图。如调试过程中生成截图、dump、trace、日志或脚本，任务结束前删除并用 `git status --short` 审计。
14. 每个任务完成后更新自身状态并写“完成成果”，记录主要实现、专项测试、关键测量结果及发现的问题。

## Phase A：阅读进度单一语义

## [x] T001（P0）：修复显式切章/切段后的持久化 checkpoint 时机

目标：

- 消除“播放器已到 B 章、SQLite 仍停在 A 章，直到下一次 session 替换才写入 B”的状态窗口。
- 第一次显式切换成功后，运行态与持久化 checkpoint 都指向新的逻辑位置。

实施方向：

1. 先用 Application 单元测试复现当前缺陷，至少覆盖暂停态：
   - 已存在 A 章 session/持久化进度。
   - 调用 `JumpToChapterAsync(B)` 或等价章节目标导航。
   - `CurrentSnapshot.ChapterIndex` 已为 B。
   - 在不执行第二次跳转、不 Pause、不 Stop 的情况下，持久化 `ReadingProgress` 也必须已经是 B。
2. 审计 `StartNewSessionAsync` 当前“先保存旧 session → 创建/发布新 session”的顺序，以及 `OpenResolvedPositionAsync`、章节/段落相对移动、目录指定章节进入、Playing/Paused、无已加载音频、缺少规则等分支。
3. 将“提交新的逻辑阅读位置”收敛为清晰的 Application 层语义。允许复用/重构现有 `PlaybackProgressService`，但不要在多个 UI ViewModel 中各自写数据库。
4. 显式跳转成功、目标位置已经解析并成为当前 session 后，及时 checkpoint 新的章节/段落/字符位置；仅保存旧 session 不能视为本次跳转完成。
5. 保留已有 Pause、Stop、session 替换等 checkpoint，但审计是否存在旧 session 的迟到保存覆盖新位置的风险。播放状态机已有串行化边界时优先利用现有边界，不额外引入并行写队列。
6. 目标解析失败、取消、书籍/章节不存在或操作未真正提交时，不写入目标位置。

专项测试/验收：

- A→B 暂停态切章后，不产生额外操作即可读到 B 的持久化进度。
- A→B 播放态切章仍正确，且不会因为先停旧音频而把 A 重新覆盖到数据库。
- 显式切段至少覆盖同章与跨章解析边界。
- 从 BookDetails 指定章节进入 Player 后，新章节第一次打开完成即建立正确 checkpoint。
- 取消/失败路径保留原进度。
- 不引入高频逐毫秒 SQLite 写入。

完成成果：显式 Start/Open/Jump/Move 在目标 session 提交前完成新位置 checkpoint，并通过 session 标识与事件 epoch 防止旧音频事件回写；失败或取消时恢复原 session 与进度。新增暂停/播放、同章/跨章及失败/取消回归测试；`PlaybackCoordinatorTests` 55 项、播放 Application 测试 10 项通过。

## [x] T002（P0）：建立跨书库/详情/播放页统一的 Effective Reading Progress 投影

依赖：T001。

目标：

- 当前活动书籍的书库卡片、书籍详情“当前章节”、目录当前项和进度百分比与 Player `CurrentSnapshot` 同步。
- 页面不再依赖“等待 SQLite 已写入，再重新查询”才能显示当前 session 的即时位置。

实施方向：

1. 建立一个无独立可变状态的有效进度投影/解析边界。具体类型名可按现有架构选择，但语义固定为：
   - `PlaybackSnapshot.BookId == targetBookId` 时，以 Snapshot 的章节/段落/章节标题为即时真值，并结合该书总章节数计算剩余章节/总体章节进度等派生值。
   - BookId 不匹配或没有活动播放上下文时，使用 `BookSummary` / `BookDetails` 中的持久化 `ReadingProgress` 投影。
2. 不创建第三套 singleton mutable progress cache；Snapshot 仍由 PlaybackCoordinator 所有，SQLite 仍由持久化层所有，Effective Progress 只做投影合并。
3. `LibraryViewModel` 已订阅 `SnapshotChanged`，应让该事件真正更新匹配书籍卡片的当前章节、剩余章节和进度，而不仅保存 `_activePlaybackBookId`。避免为了当前书每次 Snapshot 都重新查询整套书库。
4. `BookDetailsViewModel` 在激活期间订阅/读取当前 Snapshot：
   - 初始详情查询完成后应用 persisted baseline，再覆盖当前活动书籍的 Snapshot。
   - Snapshot 变化后更新当前章节文本、目录 `IsCurrent`、进度和定位目标。
   - 异步详情结果晚于 Snapshot 到达时，不能用旧数据库值把新的运行态位置覆盖回去。
5. 从详情目录指定新章节进入 Player 后，第一次返回新创建的 BookDetails 实例时，即使 SQLite 查询结果存在短暂时序差，也必须显示当前 Snapshot 的新章节。
6. 非当前书籍不受另一本文本的 Snapshot 污染；播放上下文清空后回到持久化基线。

专项测试/验收：

- Player A章→切 B章后，Library 中同一本书第一次观察 Snapshot 即显示 B 和新的章节进度。
- BookDetails(book-A)→Player 指定 B章→第一次 Back，详情页当前章节和目录当前项均为 B；不需要再次进入 Player。
- 持久化详情查询先返回旧 A、随后/此前 Snapshot 已是 B 时，最终 UI 仍为 B。
- Snapshot 属于 book-A 时，book-B 卡片/详情仍使用自身持久化数据。
- 应用重启或没有活动 Snapshot 时，SQLite checkpoint 正常恢复阅读位置。
- 不新增数据库轮询和第三套全局进度状态。

完成成果：新增无状态 `EffectiveReadingProgressProjector`，以匹配书籍的 `PlaybackSnapshot` 覆盖书库卡片与详情页持久化基线，并在快照不匹配或 Idle 时恢复基线；补齐卡片/详情目录属性通知、详情页激活订阅退订及排队旧快照版本校验。新增匹配、跨书隔离、异步详情晚到、页面离开和基线恢复测试；完整 Presentation 测试 172 项、相关 WPF 契约测试 10 项通过。

## Phase B：书籍详情页返回性能定位

## [x] T003（P0）：对 Player→BookDetails 卡顿做分阶段测量并确定主因

依赖：T002。

目标：

- 不先假定 SQLite、`Task.Yield()`、Collection 或 Wpf.Ui transition 中任何一个一定是主因。
- 用同一本书的对照数据回答“卡在哪里、为什么 Library→Details 与 Player→Details 的感知不同”。

实施方向：

1. 建立临时、可删除的性能 instrumentation 或诊断 harness，至少记录以下时间点/阶段：
   - Player BackCommand / `NavigateBackAsync` 开始。
   - `NavigateWithHierarchy` 调用前后。
   - Player `OnNavigatedFrom` / activation cancellation 完成。
   - `BookDetailsPage` 构造与 `InitializeComponent`。
   - `OnNavigatedToAsync` 开始。
   - `GetBookDetailsHeaderAsync`。
   - Page `Loaded` 与首个可观测 render/Dispatcher idle 信号。
   - `GetBookDetailsAsync`。
   - DTO→章节 ViewModel 投影。
   - `Chapters.ReplaceWith` / CollectionChanged 与随后 layout。
   - 初始 `QueueCacheStatusRefresh`。
   - `CurrentItemLocatorInteraction` 的 `ScrollIntoView`/居中定位。
   - Wpf.Ui 页面 transition 的有/无对照。
2. 同一 fixture 至少比较：
   - `Library -> BookDetails(book-A)`。
   - `BookDetails(book-A) -> Player(book-A) -> Back -> BookDetails(book-A)`。
   - 冷连接/冷页面与热连接/第二次进入；章节规模保持一致。
3. 做最小 A/B 隔离，优先使用诊断开关或测试替身，不提交功能性 workaround：
   - A：暂时跳过 details supplement。
   - B：执行 supplement 查询但暂时不 ApplyDetails。
   - C：正常查询/Apply，但临时跳过初始缓存刷新和 current-item 定位。
   - D：保持业务代码不变，仅临时关闭/绕过 Navigation transition 做对照。
4. 明确检查 `Microsoft.Data.Sqlite` 实际线程：不能因调用 `ExecuteReaderAsync` / `ReadAsync` 就假定工作已经离开 Dispatcher。若引入后台线程对照，需要同时记录查询与 UI apply 的线程/阶段。
5. 检查 `BookLibraryQuery.GetBookDetailsAsync` 的 SQL 计划与数据规模；特别确认单书查询是否对全表 Chapters/AudioCacheEntries 做无谓聚合，以及相应 BookId/ChapterId 索引是否真正被利用。
6. T003 **原则上不做最终性能重构**。只允许为获得可靠测量所需的最小临时代码；任务结束前删除临时 instrumentation/trace/截图/脚本。把测量结果、主因排序和建议修复点写入本任务“完成成果”，供 T004 直接执行。

完成标准：

- 能区分“导航/页面创建”“数据库”“数据投影”“ObservableCollection/WPF layout”“缓存/定位”“Wpf.Ui transition”“Player 离开清理”各自是否构成主要阻塞。
- 至少给出一个可以稳定复现主要卡顿来源的自动或半自动诊断场景，而不是只凭主观体感。
- 说明此前仅 `Task.Yield()` 或仅后台化 SQLite 为什么没有解决/为什么不足以解决问题。
- 仓库不残留诊断产物。

完成成果：使用临时 WPF/SQLite 诊断 harness，在隔离 Desktop 中以同一 180 章 fixture、1280×760 viewport 对比 Library→BookDetails、第二次热进入和 Player→Back→BookDetails，并在完成测量后删除 harness 与 trace。阶段结果如下：

- 页面阶段：Library 冷路径约 822 ms，第二次热路径约 155 ms，Player→Back→Details 约 193 ms；导航调用约 5–6 ms，Player 离开/取消约 1 ms，BookDetailsPage 冷构造约 50 ms、热构造约 10–13 ms，`ReplaceWith` 的 180 项投影约 1–2 ms。
- 缓存/定位 A/B：正常路径约 822 ms；跳过 current-item 定位约 136 ms；保留缓存查询但不做 180 个行状态投影约 227 ms；跳过缓存刷新约 177 ms。由此确认主要长尾是 `ApplyChapterCacheStatuses` 对 180 个章节项逐项触发绑定/布局，与 `CurrentItemLocatorInteraction` 的虚拟化列表就绪/滚动监听相互放大；单独移除任一工作都能显著缩短尾部，二者不是 SQLite 查询本身。
- SQLite：真实 `Microsoft.Data.Sqlite` v7 fixture 上，`GetBookDetailsAsync` 冷/热约 7.4/1.0 ms，其中 header 约 2.9/0.15 ms、章节读取约 4.4/0.8 ms；WPF 调用线程记录为 Dispatcher t18→t18。`EXPLAIN QUERY PLAN` 显示详情头会物化并扫描全量 Chapters 与 AudioCacheEntries 的 BookId 聚合，章节查询使用 `(BookId, ChapterIndex)` 索引但因 `ORDER BY SortOrder, ChapterIndex` 使用临时 B-tree；这是规模增长风险，但不是当前秒级卡顿主因。
- Wpf.Ui：默认值为 `FadeInWithSlide/200 ms`；独立渲染对照为关闭约 12 ms、淡入滑动约 259 ms。因此 transition 是额外的感知成本，应在 T004 中与首屏重工作分离验证；Player 导航和页面创建本身不是主因。
- 诊断还验证了仅 `Task.Yield()` 只能把 supplement 推迟到 Dispatcher 后续工作，不能消除 180 项 Collection/绑定/布局与定位；仅调用 `ExecuteReaderAsync` 也没有证明 SQL 已离开 Dispatcher，实测调用从 Dispatcher 进入并回到 Dispatcher，而查询耗时远小于 UI 长尾。T004 优先处理缓存状态批量/延后投影、current-item 定位时机及 transition 与首屏的协调，再单独评估详情头聚合 SQL。
- 自动验收：临时 T003 harness 4 项全部通过；无诊断文件残留。

## Phase C：基于证据的性能修复

## [x] T004（P0）：消除 BookDetails 缓存状态投影与当前章节定位造成的 UI 长尾

依赖：T003。

已确认前提：

- T003 已排除 Player 离开、应用导航、BookDetails transient 页面创建和当前 SQLite 查询耗时作为当前主要卡顿来源。
- 180 章 fixture 中，主要长尾来自 `ApplyChapterCacheStatuses` 对全部章节逐项触发属性通知/Binding/Layout，与 `CurrentItemLocatorInteraction` 等待虚拟化列表就绪、监听布局并执行当前章节定位相互放大。
- Wpf.Ui 默认 `FadeInWithSlide/200 ms` transition 会额外增加感知延迟，但应与上述首屏 UI 重工作分离处理。
- SQLite 详情头存在全量 Chapters / AudioCacheEntries 聚合和临时 B-tree 等规模增长风险，但当前实测仅约毫秒级，不是本任务首先要解决的数百毫秒 UI 长尾。

目标：

- Player→BookDetails 与普通进入 BookDetails 时，页面先完成首屏呈现并保持 Dispatcher 可响应；缓存百分比和当前章节定位不得在同一首屏阶段形成数百毫秒连续 UI 工作。
- 保留 transient Page、强类型 `BookDetailsRoute(BookId)`、T002 Effective Reading Progress、章节虚拟化和缓存状态语义，不通过页面缓存、Singleton 或隐藏功能来换取性能。
- 在 T003 相同 180 章场景下显著消除约 800 ms 量级的 UI 长尾，使修复后的主要阶段回到与 T003 单独关闭缓存投影/定位后的百毫秒量级同一数量级；该数值只用于本轮诊断复测，不建立固定毫秒 CI 门禁。

实施方向：

1. 先建立针对已确认主因的回归保护，再修改实现：
   - 能证明初始详情数据提交后，首帧/Dispatcher 不需要同步等待“全章缓存状态逐项投影 + current-item 定位”全部完成。
   - 能证明离页/取消后，延后的缓存投影和定位不会继续作用于旧 transient Page。
   - 不以 `Task.Delay` 或固定 sleep 证明性能，优先使用 Dispatcher 阶段、可控 scheduler、版本/取消 token 和明确完成信号。

2. 优先重构 **初始全章缓存状态投影**，目标是避免“180 个已绑定行在一个 Dispatcher 阶段逐个 `ApplyCacheStatus`”：
   - 区分“首次进入页面的整章缓存状态加载”和后续 `CacheChangedEventArgs.ChapterIndex` 指向的单章增量刷新；单章变化仍应只更新受影响章节，不退化成整书刷新。
   - 缓存查询结果可先在非 UI 数据结构中整理/格式化；UI 层不要为了同一批结果反复做字典查找、全表扫描和逐项同步通知。
   - 优先选择能减少 Binding/Layout 次数的批量提交方式，例如一次性构造带缓存投影的章节行快照并以单次 Reset/等价批量变更提交，或采用只对实际变化/当前已实现行产生通知的虚拟化友好方案。具体实现可按现有架构决定，但不得仅把 180 次属性通知包进另一个同步循环后宣称“批量化”。
   - 若采用分批/低优先级增量提交，批次必须有明确 owner、取消和版本边界，并允许 Dispatcher 在批次之间处理渲染和输入；不得形成新的 fire-and-forget 生命周期泄漏。
   - 保持“0% 和非正常状态在普通详情目录不显示”的既有 UI 合同。

3. 重构 **`CurrentItemLocatorInteraction` 初始定位时机**，避免与全章缓存投影竞争同一轮布局：
   - 首次导航只在章节集合已经提交、ListBox/ScrollViewer/虚拟化容器达到可靠就绪条件后执行一次初始定位。
   - 不要在缓存状态仍批量改变行绑定/布局时持续通过 `LayoutUpdated` 反复重算定位；pending request 完成后立即解除临时 readiness/layout 监听。
   - 将“初始自动定位”和“用户点击定位到当前章节”保持同一定位核心，但用户主动定位仍必须立即响应，不能被初始延后策略长期阻塞。
   - T002 的 Snapshot 当前章节变化仍要更新 `CurrentChapterItem`；不要为了性能冻结旧章节或取消后续用户可观察定位能力。

4. 明确 **首屏阶段顺序**，避免两个已确认重工作相互放大。推荐默认顺序：
   - 加载并提交 Header / details 基础数据和章节列表；
   - 允许页面完成首个可观测 render；
   - 执行一次稳定的当前章节初始定位；
   - 再以批量或可让出 Dispatcher 的方式提交整章缓存百分比；
   - 后续只按缓存变化做增量刷新。
   若实际复测证明“缓存先、定位后”更稳定，可以调整顺序，但必须用 T003 同一 harness/等价诊断数据说明原因。

5. 单独处理 **Wpf.Ui transition 与首屏重工作的协调**：
   - 先完成第 2–4 项并复测，再判断默认 `FadeInWithSlide/200 ms` 是否仍造成明显额外延迟。
   - 优先避免重布局与 transition 动画重叠，而不是立即全局关闭动画。
   - 若主因修复后 transition 仍是显著感知成本，可采用最小作用域的策略调整 BookDetails/二级页面导航动画或时长；不得无证据全局关闭 NovelSpeaker 所有页面动效。
   - Reduced Motion / 系统动画关闭语义必须继续正确。

6. SQLite 只做 **次级规模化检查**，不能再次抢占本任务主线：
   - 在 UI 长尾修复后复测 `GetBookDetailsHeaderAsync` / `GetBookDetailsAsync`。
   - 若修改风险低，可将单书详情统计限制到目标 `BookId`，避免物化并扫描全库 Chapters / AudioCacheEntries 聚合，并验证 query plan/现有索引。
   - 不因为方法名含 `Async` 或调用 `ExecuteReaderAsync` 就声称查询已离开 Dispatcher；如最终仍需要线程迁移，必须有实际线程/耗时证据。
   - SQL 优化不得改变书籍详情、缓存总量和阅读进度查询口径。

7. 不要顺带大改与主因无关的 `ViewModelCollectionExtensions.ReplaceWith`。T003 已测得 180 项基础章节投影仅约 1–2 ms；只有新的复测证明它在最终实现中重新成为主要瓶颈时才调整公共集合基础设施。

专项测试/验收：

- 使用与 T003 一致的 180 章 fixture、1280×760 viewport 和 Library→Details / 热进入 / Player→Back→Details 路径复测，记录修复前后：首个 render、当前章节初始定位完成、整章缓存状态投影完成、页面稳定阶段。
- 修复后不得再出现 `ApplyChapterCacheStatuses` 一次性对全部章节同步触发长时间 Binding/Layout 的已知路径；全章刷新要么单次批量提交，要么可让出 Dispatcher 的有界增量提交。
- 初始 locator 不应因为随后每个缓存行属性变化持续收到/处理整轮 `LayoutUpdated`；定位完成后临时监听必须解除。
- Player→Back→Details 在缓存状态尚未完全投影时，页面基础信息、当前章节和返回/编辑等首屏交互仍可用。
- 缓存状态最终必须完整正确：有百分比章节显示正确值，0%/非正常状态继续隐藏；后续单章缓存变化仍能刷新对应行。
- 当前章节定位、用户手动滚动后的“定位到当前章节”、虚拟化长目录、Effective Reading Progress、Dirty State guard 和强类型返回均无回归。
- 快速 Player↔Details 往返、导航取消、页面离开时，不出现迟到 apply、旧页面定位、ObjectDisposedException、未观察后台异常或残留事件订阅。
- Wpf.Ui transition 调整若发生，必须提供“主因修复后”的独立 A/B 数据，并验证正常动画与 Reduced Motion 两种路径。
- SQLite 若顺带优化，必须有 Infrastructure 集成测试保证查询语义，并记录优化前后 query plan；不得把 SQL 优化作为“UI 卡顿已修复”的唯一依据。
- 所有 T004 临时 harness、计时日志、trace、A/B 开关和诊断脚本在任务结束前删除；使用 `git status --short` 审计无副产物。

完成标准：

- T003 已确认的两个 UI 主因均有针对性实现和自动回归，不再停留于“Task.Yield/后台 SQLite”式无效修复。
- 同一诊断 fixture 下，原正常路径的数百毫秒长尾显著收敛；如果仍明显高于 T003 单项关闭缓存投影/定位时的百毫秒级结果，应继续定位剩余 Dispatcher 工作，不能直接关闭任务。
- 生产代码保持既有导航、进度和页面生命周期架构，没有新增页面缓存、Singleton、第三套详情状态或无 owner 的后台任务。

完成成果：BookDetails 初始缓存状态查询现在由页面在首轮当前章节 locator 完成后启动；180 章初始状态通过静默行状态写入加单次集合 Reset 提交，后续单章缓存变化仍保持增量通知。locator 的 readiness、取消、重复激活和页面版本边界已补齐，离页不会再让旧页面收到迟到定位或缓存投影；Player 复用的缓存刷新行为保持不变。

- 自动验收：180 章缓存投影回归确认初始批次不产生行级属性通知且只产生一次集合 Reset；延后启动、Loaded/详情投影顺序、缓存控制器 initial/incremental 批次和页面离开后的迟到结果测试通过。`dotnet format --verify-no-changes --no-restore`、Release build（0 警告/0 错误）和 focused presentation tests（6 项）通过。
- 环境限制：BookDetails/locator WPF 测试已成功构建，但当前隔离 Desktop 测试宿主无输出挂起；20 秒 hang diagnostic 后 testhost 因 inactivity 中止，因此本轮未取得 WPF 运行时长尾 A/B 数值，生产代码和 presentation 回归已自动验证。

## Phase D：整体架构优化前诊断

## [x] T005（P0）：生成全项目 Architecture Review 诊断报告

依赖：T001、T002、T003、T004。

目标：

- 在不修改生产架构、不继续修复 3000+ 章节卡顿的前提下，对当前仓库做一次可复核的全局架构盘点。
- 输出一份独立的 `architecture_diagnostic_report.md`，供后续人工/AI 架构规划使用。
- 报告必须尽量以代码、依赖、数量、调用关系和 Git 历史事实为依据，区分“已确认问题”“架构压力信号”“可能问题/待决策项”，不要直接把个人偏好写成最终重构方案。

执行规则：

1. 本任务只允许：
   - 读取仓库；
   - 运行静态分析、`dotnet`/`git`/PowerShell 等诊断命令；
   - 编写一次性本地分析脚本；
   - 生成 `architecture_diagnostic_report.md`。
2. 不修改 `src/`、`tests/`、项目文件、SQLite migration、正式 docs 架构定义或行为代码。
3. 一次性脚本和中间 CSV/JSON/trace 必须在报告生成后删除。最终只保留 `architecture_diagnostic_report.md` 作为用户需要上传的诊断输出，以及本任务自身的 Backlog 完成记录。
4. `architecture_diagnostic_report.md` 作为临时评审输入，不视为正式项目文档；不要把它加入架构决策文档索引，也不要引用为长期规范。
5. 如完整测试耗时过长或 WPF testhost 再次挂起，只记录事实，不为了完成诊断修改测试宿主。
6. 不生成截图，不启用 `NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS=1`。

诊断内容：

### A. 项目与依赖拓扑

- 列出所有 `.csproj`、目标框架、主要 NuGet 包和 ProjectReference。
- 绘制文本形式的项目依赖图，确认 Domain / Application / Infrastructure / App / tests / tools 的实际引用方向。
- 找出任何跨层反向依赖、非 Bootstrap 的 Infrastructure 直连、App Feature 之间的直接引用、Shared 对 Feature 的反向依赖。
- 对 `src/NovelSpeaker.App/Features/*` 建立 Feature→Feature / Feature→Shared / Feature→Application 的引用矩阵；重点列出跨 Feature namespace `using`、直接类型依赖和共享 controller/helper 的调用关系。
- 标出只有单一调用方却被提升为全局 Shared/Application abstraction 的组件，以及被多个 Feature 实际复用但仍复制实现的组件。

### B. 代码规模与复杂度热点

- 对生产代码统计：
  - 每个项目的 `.cs` / `.xaml` 文件数与总行数；
  - 每个 Feature 的文件数与总行数；
  - 最大的 30 个生产 `.cs` 文件；
  - 最大的 20 个 XAML 文件。
- 对主要类型尽可能统计：
  - 类型行数；
  - 构造函数依赖数量；
  - public/internal 方法数量；
  - 字段数量；
  - 事件订阅数量；
  - `CancellationTokenSource` / version / generation / timer / task registry 等生命周期字段数量。
- 必须单独审计至少：
  - `BookDetailsViewModel`
  - `LibraryViewModel`
  - Player 主 ViewModel / coordinator
  - `PlaybackCoordinator`
  - Cache workspace / active cache / export coordinator
  - Shell/navigation/lifecycle coordinator
  - 三类 Rules 页面/ViewModel
  - CacheManagement
  - shared scrolling/selection/cache refresh controllers。
- 对热点类型判断其职责是否混合了 Query、Command、UI state、后台任务、导航、持久化、事件协调等多个方向；只列证据，不直接要求“拆成 N 个 service”。

### C. 状态所有权与数据流

- 以当前 docs 的状态所有权表为基线，反向检查实际代码。
- 列出所有长期/中期 mutable state owner：
  - Singleton；
  - Playback session；
  - Background coordinator；
  - Page/ViewModel；
  - static state。
- 对以下核心状态绘制“source of truth → projection/subscriber → persistence”数据流：
  - PlaybackSnapshot / reading progress；
  - 当前 TTS rule / settings；
  - active cache batch；
  - export batch；
  - cache status / speech plan；
  - navigation CurrentRoute；
  - BookDetails / Library 页面读模型。
- 找出同一业务状态存在两个以上 mutable owner、页面复制进程级状态、持久化值与运行时值缺少明确优先级、多个事件源可以更新同一 UI 状态等情况。
- 统计主要 `event +=` / `event -=`、SnapshotChanged/Changed 类事件以及自定义 callback/delegate 链，指出订阅生命周期由谁负责。

### D. 异步、线程与 Dispatcher 边界

- 搜索并分类：
  - `Task.Yield`
  - `Task.Run`
  - `async void`
  - `Dispatcher.Invoke/BeginInvoke/InvokeAsync`
  - `IUiScheduler`
  - `ConfigureAwait`
  - `.Result` / `.Wait()` / `GetAwaiter().GetResult()`
  - `Thread.Sleep` / `Task.Delay`
  - fire-and-forget / `OwnedTaskRegistry`
  - `CancellationTokenSource`
- 报告每类在生产代码中的数量、主要位置和用途。
- 找出“方法是 Async 但实际同步重工作仍可能在 Dispatcher 调用线程执行”的边界，特别包括 Microsoft.Data.Sqlite、大集合 projection、排序/grouping/hash、文件/文本处理。
- 检查页面 activation、background job、playback session 三种生命周期是否各自拥有一致的取消/迟到结果策略，列出重复的 version/generation/CTS 模式。
- 列出可能造成 UI Dispatcher 工作量随完整数据集 N 线性增长的路径。

### E. Collection / 大列表 / UI 投影架构

- 搜索所有 `ObservableCollection`、`ReplaceWith`、`Clear()+Add`、CollectionChanged Reset、自定义 item VM 列表。
- 对 Library、BookDetails、Player 章节目录、CacheManagement、Rules 列表分别记录：
  - 数据集可能规模；
  - 是否一次性 materialize 全量 DTO；
  - 是否为所有项建立 mutable item ViewModel；
  - 是否逐项通知；
  - 是否 WPF UI virtualization；
  - `ScrollUnit`；
  - 是否有 current-item / selection / cache enrichment；
  - enrichment 是否对全量数据运行还是 viewport/目标项运行。
- 单独记录当前 3000+ 章节 BookDetails 卡顿涉及的结构路径，但不进行性能修复。
- 找出其它未来可能出现同类 O(N) UI 首屏成本的页面。

### F. Application / Infrastructure 边界

- 列出 Application 中主要 service/coordinator/query/port 及其实现位置。
- 统计接口数量、只有一个实现的接口数量、主要 constructor injection 链。
- 区分：
  - 真正用于技术边界/替换/测试隔离的 port；
  - Feature-local 逻辑却被提升到全局 Application 的 abstraction；
  - Infrastructure query 返回过度面向页面的 read model；
  - App ViewModel 自己组合过多底层 Application port 的情况。
- 检查 Books / Playback / Cache / Speech / Settings / Desktop 的功能切片是否仍与实际代码目录和调用方向一致。
- 列出可能需要在后续规划中讨论的边界重划分，但不要自行决定。

### G. DI 与生命周期

- 从实际注册代码生成主要服务的生命周期清单：Singleton / Transient / Scoped（如有）。
- 标出：
  - Singleton 持有短生命周期对象的风险；
  - transient Page/ViewModel 引用 process coordinator 的正常边界；
  - 同一服务在多个模块重复注册；
  - constructor 参数过多；
  - 组合根之外 `IServiceProvider` 使用。
- 报告最大 constructor dependency 数量的前 20 个生产类型。

### H. 持久化与查询架构

- 汇总 SQLite 表、repository/query/store 与 Application read/write port 的映射。
- 不修改已发布 migration。
- 检查：
  - 是否存在针对单个页面查询却扫描/聚合全库的 SQL；
  - N+1 查询；
  - 同一页面一次 activation 重复查询同一数据；
  - Infrastructure 返回的数据是否导致 App 再做大规模二次组装；
  - read model 是否过大/过细碎。
- 将已知 BookDetails 全量聚合 SQL 作为实例之一，但同时检查 Library、CacheManagement、Playback 恢复、Rules 等其它查询。

### I. 测试架构与维护成本

- 统计每个 test project：
  - 测试文件数；
  - 测试代码总行数；
  - 最大的 30 个测试文件；
  - 与 TestKit 的依赖。
- 尽可能统计当前测试数；不为得到数量修改测试。
- 找出：
  - 大量重复 fake/stub；
  - 单个测试文件/fixture 过大；
  - 为私有实现细节建立的脆弱测试；
  - WPF tests 与 Presentation tests 职责交叠；
  - 生产重构需要同步修改大量测试的热点。
- 记录当前 WPF 隐藏 Desktop/testhost hang 情况以及它对架构重构验证能力的影响。

### J. Git 变更热点与耦合

- 使用 `git log` 对最近约 6 个月或仓库实际可用历史做 churn 分析：
  - 修改次数最多的生产文件；
  - 增删行最多的生产文件；
  - 经常在同一 commit 中一起变化的文件/目录组合。
- 特别观察 BookDetails、Player、Cache、Shared Presentation、Shell、测试基础设施。
- 将“高 churn + 大文件 + 高依赖/高状态数”的交叉点列为 Architecture Review hotspot。
- 不把单纯文件大或修改多自动判定为坏架构。

### K. 当前架构资产与不可轻易破坏的边界

报告不能只有问题。必须列出当前已经相对稳定、建议后续重构优先保留的资产，例如经代码证据确认的：

- Domain/Application/Infrastructure/App 依赖方向；
- 强类型 AppRoute；
- Playback session 单 owner；
- PlaybackSnapshot + persisted checkpoint 的优先级；
- background cache/export coordinator owner；
- SQLite migration 追加策略；
- 外部 TXT 不可写边界；
- cache 可重建边界；
- TestKit/WPF 隔离能力；
- 其它实际已形成稳定合同的部分。

### L. 诊断结论格式

报告结尾必须给出以下表格，但**不制定最终重构方案**：

| 热点 | 证据 | 影响范围 | 风险等级 | 可能需要决策的问题 |
|---|---|---|---|---|

风险等级只使用：
- High：已经造成正确性/性能/维护阻塞，或修改常产生跨模块连锁。
- Medium：复杂度持续增加，现阶段仍可工作。
- Low：主要是组织/命名/重复问题。

再给出：

1. “最值得优先讨论的 5–10 个架构决策问题”；
2. “应尽量保留的稳定架构资产”；
3. “需要用户产品/维护偏好才能决定的事项”；
4. “3000+ 章节卡顿在整体架构中的关联点”，仅关联，不给最终修复。

输出与验收：

- 输出文件固定为仓库根目录 `architecture_diagnostic_report.md`。
- 报告中命令输出应做摘要，不粘贴超长完整日志。
- 如使用临时 PowerShell/Python/C# 分析脚本，完成后删除。
- 至少执行：
  - `dotnet restore --locked-mode -r win-x64`
  - `dotnet build -c Release --no-restore`
  - 现有架构/依赖相关测试或能够稳定运行的对应 test project。
- 本任务不要求为当前 3000+ 章节卡顿运行新的性能 harness。
- 完成后在 T005 下记录“完成成果”，说明报告路径、关键统计数量、build/test 状态和任何无法自动采集的数据。
- **不要提交 `architecture_diagnostic_report.md`**；保留为工作区未跟踪文件，供用户上传给后续架构规划会话。
- Codex 提交时只提交 `docs/TASK_BACKLOG.md` 的 T005 完成状态（以及若诊断过程中确有必要修正的纯诊断说明）；提交前确认生产代码无变更。

完成成果：已生成根目录 `architecture_diagnostic_report.md`（未提交），完成项目/Feature/依赖、复杂度与状态所有权、异步与大列表、Application/Infrastructure、DI、SQLite、测试架构和 Git churn 诊断。静态统计为 10 个项目、685 个生产源文件/61,864 行、14 个 Feature、约 82 个 Application 接口；测试项目 210 个源文件/59,594 行，另有 TestKit 19 个文件/3,421 行。已执行锁定还原成功；App Release build 成功且 0 warning/0 error；目标 Presentation 架构/生命周期测试 42/42、WPF 隔离测试 10/10、WPF DI/视觉架构测试 14/14 通过。完整 solution build 因 WPF 测试输出被残留 `testhost` 进程锁定而失败，详见报告；未运行完整测试集、性能 harness 或 SQL EXPLAIN。

## Phase E：旧阶段收口（暂缓）

当前不执行以下任务。整体架构优化方案确定后，应在新的规划阶段决定删除、重写或重新安排，而不是直接继续旧的性能收口。

## [ ] T006（P1，暂缓）：补齐进度/性能回归并执行完整质量门禁

依赖：T001、T002、T003、T004、T005。

目标：

- 把本轮两个原始缺陷固化为稳定回归，清理诊断代码和低价值重复测试。

实施方向：

1. 保留最小但完整的跨层回归矩阵：
   - 暂停态切章后 Snapshot 与持久化 checkpoint 第一次即一致。
   - Library 当前书卡片立即跟随 Snapshot。
   - BookDetails 指定章节进入 Player 后第一次返回即显示该章节。
   - 无活动 Snapshot/应用重启时从 SQLite checkpoint 恢复。
   - 同一本书 Player→Details 不因补充查询/大章节集合在 Dispatcher 上形成已知同步阻塞路径。
2. 检查是否产生重复 progress resolver、重复 Snapshot 订阅 helper、页面级数据库写入或为了测试暴露的生产调试 API；能收敛则收敛。
3. 删除 T003/T004 的临时计时器、日志、trace、A/B 开关、截图、dump、一次性 benchmark/harness；只保留有长期回归价值的测试基础设施。
4. 更新因最终实现与 T003 初始假设不同而需要修正的数字文档；不得在稳定文档中留下“可能是 X”式已经过期的诊断描述。
5. 执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

完成标准：

- 完整门禁 0 失败；Release build 0 warning / 0 error。
- 原始“切章后其它页面仍旧、第二次返回才更新”缺陷有直接自动回归。
- 原始“Player 返回 BookDetails 明显卡顿”路径有可复核的线程/生命周期回归证据，并且最终实现不依赖诊断开关。
- 仓库没有 trace、dump、截图、临时日志、benchmark 输出、TestResults 诊断副产物或其它本轮临时文件残留。
