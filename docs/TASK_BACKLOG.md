# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前阶段处理 **阅读进度一致性与书籍详情页返回性能**。当前 `dev` 基线为 `05f06977c6976848b6f0e70bb953e9f84535a57c`。

本阶段解决两个相互关联但必须分层处理的问题：

1. 播放页已经切换到新章节时，书库卡片和书籍详情仍可能显示旧章节；从详情目录进入新章节播放后第一次返回仍显示旧章节，第二次进入/返回才更新。
2. Player 通过强类型 `BookDetailsRoute(BookId)` 返回时会创建新的 transient 详情页实例；当前返回过程存在明显卡顿。此前仅把详情补充查询视为“async”或简单搬到后台仍不足以证明问题已解决，必须先拆分测量导航、页面创建、SQLite、投影、集合更新、定位/缓存刷新和 Wpf.Ui transition 的实际成本，再按证据修复。

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

## [ ] T001（P0）：修复显式切章/切段后的持久化 checkpoint 时机

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

## [ ] T002（P0）：建立跨书库/详情/播放页统一的 Effective Reading Progress 投影

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

## Phase B：书籍详情页返回性能定位

## [ ] T003（P0）：对 Player→BookDetails 卡顿做分阶段测量并确定主因

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

## Phase C：基于证据的性能修复

## [ ] T004（P0）：按 T003 测量结果消除 BookDetails 返回主线程长阻塞

依赖：T003。

目标：

- Player 返回详情页时尽快出现可交互的详情页面/轻量摘要，重工作不会形成明显 UI freeze。
- 保持 transient Page + 强类型 `BookDetailsRoute` 架构，不靠页面缓存绕过真实加载成本。

实施方向：

1. 先读取 T003 的“完成成果”，只修复有测量证据的主瓶颈；不要把所有候选优化一次性混入。
2. 如果 SQLite/同步 I/O 是主要成本：
   - 让真实同步数据库工作在明确的非 Dispatcher 执行边界运行；不要用 `Task.Yield()` 冒充后台调度。
   - 单书查询限制到目标 `BookId`，避免为了一个详情页聚合整库数据；按 query plan 增补/复用必要索引。
   - 保持连接/reader 的线程所有权清晰，不跨线程搬运活动 SQLite 对象。
3. 如果 DTO→章节 VM / `ObservableCollection.Clear()+N×Add()` / layout 是主要成本：
   - 纯投影尽量在 UI 提交前完成。
   - UI 侧采用能够减少大量逐项 CollectionChanged/layout 的批量替换方式，同时保持虚拟化、绑定和 current-item 语义。
4. 如果初始缓存完整度刷新或 current-item locator 是主要成本：
   - 将非首帧必需工作推迟到页面已呈现之后，并保留取消、版本和离页清理。
   - 不允许迟到结果作用于已离开的 transient Page。
5. 如果 Wpf.Ui transition 是显著放大器：
   - 仅在确认 transition 本身占据主要 UI 时间后调整该页面/宿主的 transition 策略。
   - 不以全局禁用所有动效作为默认修复。
6. Header/轻量摘要与详情 supplement 保持清晰阶段：页面先获得可用身份与基础信息；章节目录/缓存等补充数据异步完成。不要为了“首帧快”显示错误的当前章节，T002 的 Effective Progress 仍必须即时覆盖。

专项测试/验收：

- 使用 T003 同一对照场景复测，记录修复前/后主要阶段数据。
- 可控 slow query/slow projection 场景证明页面首帧/Dispatcher 不被重工作同步卡住；避免脆弱的固定毫秒阈值。
- `BookDetailsRoute(BookId)`、Dirty State guard、目录虚拟化、当前章节定位、缓存百分比和详情编辑行为无回归。
- 快速往返 Player/Details、导航取消和页面离开时，不发生迟到 apply、ObjectDisposedException 或后台异常未观察。
- 不新增 Page/ViewModel Singleton、Navigation cache workaround 或重复详情状态缓存。

## Phase D：集成回归与收口

## [ ] T005（P1）：补齐进度/性能回归并执行完整质量门禁

依赖：T001、T002、T003、T004。

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
