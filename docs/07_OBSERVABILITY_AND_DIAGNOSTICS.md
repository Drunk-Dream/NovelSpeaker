# Observability 与诊断

## 1. 定位

NovelSpeaker 使用三套职责清晰的本地诊断能力：

1. **生产日志**：回答“发生了什么异常/失败”。
2. **性能遥测**：回答“长期真实使用中哪里值得优化”。
3. **诊断会话**：回答“这一次可复现问题发生时，应用现场是什么样”。

三者均以本地数据为主，不自动上传。诊断能力不能成为业务故障的新来源。

## 2. 总体架构

业务模块只描述稳定操作边界，Logging、Telemetry 与 Diagnostic Session 各自拥有独立持久化与失败策略：

```text
Feature / Application
        ↓
thin Observability API
        ├─ Performance Telemetry
        └─ Diagnostic Session

Production Logging ── correlation ──┘
```

原则：

- 不引入完整 OpenTelemetry 或通用 EventBus。
- Telemetry 与 Diagnostic Session 可以共享稳定 operation instrumentation，但各自独立开关、粒度、生命周期和持久化。
- Logging / Telemetry / Diagnostics 任一失败不得使业务操作失败。
- 诊断系统自身的用户可感知失败必须进入生产日志，记录稳定操作名、失败阶段、异常类型/HRESULT/脱敏异常链；不得记录完整本地路径或用户内容。
- 高频内部方法调用不等于稳定诊断操作。Instrumentation 必须围绕用户/系统操作边界，避免诊断系统自身制造显著负载。

### 故障与 Process 生命周期边界

平台异常入口只负责发现原始故障并交给一个薄的应用级故障边界；故障的稳定分类、Process 最终退出原因和各诊断视图不得由不同层各自重新推断。

```text
WPF / .NET failure source
        ↓
process failure boundary
        ├─ Production Logging：详细异常证据
        ├─ Diagnostic Event：低基数故障事实
        └─ Process lifetime：最终退出原因
                         ↓
                 orderly shutdown
                         ↓
              Diagnostic Session Store
```

长期规则：

- Startup failure、运行期 UI fatal failure、其它运行期故障必须保持不同语义，运行期故障不得复用 startup failure 事件名或 operation；是否属于 fatal 由稳定故障语义决定，不能仅根据异常入口名称推断。
- “是否按完整关闭流程退出”与“为什么退出”是两个概念；fatal failure 即使随后完成有序 shutdown，也不得被记录成 normal exit。
- Process 最终退出原因只有一个 owner。Diagnostic Session Store 只持久化上层给出的退出事实，不负责判断正常/致命；只有进程突然消失、无法主动报告结束时，Session 恢复逻辑才根据缺失的 Process 结束记录推断 unexpected termination。
- Fatal failure 在生产日志中保存脱敏异常链和 stack trace；Diagnostic Session 只保存稳定、低基数的故障来源/严重程度/后续动作，不复制完整异常栈，也不建立第二套日志。
- 故障边界保持薄，不演化成通用 EventBus、异常路由框架、Crash Database 或复杂 Process 状态机。

## 3. 生产日志

### 产品行为

- 默认开启。
- 结构化 JSONL，本地轮转保存。
- 主要记录启动/退出、Warning、Error、Critical、关键恢复/降级与少量重要生命周期。
- 性能慢但成功不自动转为 Warning。

### 写入原则

- 业务线程非阻塞 enqueue。
- bounded queue + 单后台 writer + 批量 append。
- overflow 优先牺牲低价值记录，不阻塞业务。
- dropped count 作为基础设施健康信息，不递归写日志。
- 正常退出有限 drain/flush；崩溃 best effort。

### 故障记录

- 启动阶段故障使用 startup-specific 事件；应用已经进入正常运行阶段后发生的未处理异常使用 runtime failure 事件，不借用启动事件名。
- 生产日志是完整异常证据的 owner，保存允许范围内的异常类型、HRESULT、脱敏异常链和 stack trace。
- 同一个 fatal failure 只分类一次；不同 sink 通过 process/session/activity correlation 关联，不分别发明新的故障含义。
- 日志写入失败不得阻止应用继续执行原本的错误处理或关闭流程。

## 4. 性能遥测

### 产品行为

- 默认关闭，由用户主动开启。
- 只保存在本地，不自动上传。
- 关闭后立即停止新采集，但已有数据继续保留。
- 用户可以清除历史数据。
- 导出诊断信息在遥测关闭时仍可使用。
- Settings 只提供 Toggle、清除、导出和少量说明，不显示监控状态面板。

### 数据模型

普通遥测保存 Counter、Histogram 与 Gauge 的低开销聚合，不保存每次原始普通事件。内部仍按约 1 分钟窗口写入 JSONL，运行时持久化格式与导出交换格式保持分离。

Operation 指标继续由稳定操作完成事件驱动：

- `operation.count` 记录稳定操作完成次数；
- `operation.duration` 记录稳定操作耗时；
- operation 名称必须准确描述实际被测边界，不能用“query”等名称描述实际上只覆盖 connection open 的操作；
- 需要区分主要页面、Cache 等功能边界时，优先使用有限、稳定且语义明确的 operation vocabulary，而不是记录任意 Route、BookId、ChapterId 或其他高基数标签。

进程资源指标与 Operation 采集解耦：

- 性能遥测开启期间，以**约 60 秒**为内部采样周期独立读取 CPU、Working Set 与 Managed Heap；
- 即使期间没有 Navigation、TTS、Cache 等 Operation，也继续低频采样并形成只有资源指标的一分钟窗口；
- 不再在每次 Operation 完成时附带读取 CPU/内存；
- 开启或重新开启遥测时重新建立 CPU 计算基线，不把关闭期间的时间纳入下一次 CPU 利用率；
- 60 秒属于当前内部低频策略，不在 UI 暴露为用户可配置项。

每个新写入的一分钟窗口至少保留：

- `windowStartUtc` / `windowEndUtc`；
- `appVersion`；
- 当前进程的匿名 `processInstanceId`；
- 当前窗口实际有样本的 metric aggregates。

`processInstanceId` 只用于区分一次应用进程生命周期和进行时间关联，不作为 Metric tag，不代表用户身份，也不得跨进程复用。

Histogram bucket 应覆盖亚毫秒级本地存储操作到数十秒级网络/TTS/Cache 操作。具体 bucket 是实现策略，可以随实际数据调整，不形成 UI 或外部长期合同。

`UiDispatcherStall` 只代表具有“等待/阻塞异常”语义的调度器观测，不得把每一次普通 Dispatcher 调用都记录成 stall。

标签和 operation vocabulary 必须保持低基数并由 Registry 统一声明，禁止 BookId、ChapterIndex、路径、URL、SQL text、用户内容等高基数或敏感数据。

### 本地保留

内部策略采用：

- 按日 JSONL；
- 单文件大小分卷；
- 约 30 天保留；
- 总目录约 64 MiB 上限；
- 从最旧文件开始清理。

进程资源低频采样会使“遥测开启但应用空闲”的时间段也产生资源窗口，因此不再要求所有窗口都必须由用户操作触发。目录 retention/capacity 继续作为总体大小边界。

具体限制属于内部策略，不在 UI 展示，也不给用户配置。

## 5. “诊断信息”导出

用于常规优化的诊断信息导出**当前本地仍保留的全部性能遥测**，不再额外人为截取固定 14 天范围，也不让用户选择复杂时间范围。

用户点击导出后打开 Windows **保存文件对话框**，只用于选择最终 ZIP 的保存位置和文件名；应用自行读取内部 Telemetry/Logs 数据，不要求用户选择内部诊断文件或目录。

默认文件名使用本地时间：

```text
NovelSpeaker-Diagnostics-yyyyMMdd-HHmmss.zip
```

ZIP 保持简单：

```text
NovelSpeaker-Diagnostics-*.zip
├─ summary.md
├─ telemetry.json
├─ logs.jsonl
└─ environment.json
```

### `telemetry.json`

不再把 summary、schema 和一分钟时间序列拆成多个性能遥测文件。单个版本化 `telemetry.json` 同时保存：

```text
telemetry.json
├─ schemaVersion
├─ generatedAtUtc
├─ coverage
├─ collection
├─ metricDefinitions
├─ aggregates
└─ windows
```

其中：

- `coverage`：此次实际导出的最早/最晚遥测时间及涉及版本等覆盖信息；
- `collection`：窗口大小、资源采样间隔、窗口数、dropped window count、degraded 状态等采集元数据；
- `metricDefinitions`：指标名称、类型、单位、描述、Histogram bucket 和允许的有限维度，用于让人或 AI 正确解释数据；
- `aggregates`：按版本/日期/稳定 metric/operation 等形成的便捷汇总视图，避免每次分析都重新计算全部窗口；
- `windows`：保留原有约 1 分钟时间结构，包括 `appVersion`、`processInstanceId` 和该窗口的 metric aggregates。

`aggregates` 和 `windows` 是同一份遥测事实的不同导出视图，不是两套运行时真值。内部仍只维护现有窗口持久化数据。

旧版本内部窗口缺少新字段时，导出读取必须安全降级；不得伪造 `processInstanceId`。未发布的内部遥测 JSONL schema 不承诺长期兼容，但升级不能因为旧记录而导致整个导出失败。

### 关联日志与摘要

- `logs.jsonl` 继续只提供异常、降级和必要生命周期事实，不把“性能慢”自动转换为 Warning。
- 日志优先按照实际导出的遥测 coverage 和 `processInstanceId` 进行关联；不为了性能分析新增高频性能日志。
- `summary.md` 只提供客观 coverage、版本、进程数、窗口数、dropped/degraded 等概况，不自动判断性能问题原因。

### 导出可靠性

- 先写同目录临时文件，成功完成并关闭 ZIP 后再原子替换/移动到目标文件。
- 读取正在轮转或存在单条损坏记录的日志时采用 best effort；单个日志文件不可读不得使整个导出失败。
- 导出失败必须记录生产日志中的结构化诊断事件，UI 只显示简短用户消息。
- 导出目标路径属于用户明确选择的数据，不写入结构化日志或脱敏摘要。

## 6. 诊断会话

### 生命周期与 owner

诊断会话的运行态由独立的 Diagnostic Recording Controller / Session owner 维护，悬浮控制条只是状态视图，不拥有 Session 真值。

- 从 Settings 打开诊断工具不立即采集。
- 用户点击“开始诊断”后才创建 Session 和 `.nsdiag`。
- Active Session 可以跨正常退出、重启甚至升级持续。
- Process 结束不等于 Session 结束。
- 只有用户点击“结束”才将 Session 标记为 Ended。
- 已结束 Session 不重新打开续写；“重新开始”创建新 Session。
- Active Session marker 只定位权威 `.nsdiag`，不是第二份状态数据库。
- 应用启动恢复 Active Session 后必须自动恢复明显可见的悬浮控制条，不允许后台继续采集而没有录制提示。

Process 生命周期是 Session 内的独立事实：

- 每个 Process 有独立 `processInstanceId`、开始时间、结束时间和稳定退出原因。
- 有序 shutdown 时，Process lifetime owner 将已经确定的退出原因传给 Session；Store 不把“成功执行 shutdown”自动等价为 `normal-exit`。
- 能够进入有序 shutdown 的 UI/其它 fatal failure 即使完整执行关闭流程，也必须保留对应 fatal exit reason；来不及主动留下结束记录的进程级崩溃继续由恢复逻辑识别为 unexpected termination。
- 如果 Process 没有留下结束记录，下次恢复 Active Session 时才将上一 Process 推断为 unexpected termination，并标记 Session 曾经历意外 Process 结束。
- Session 的 `EndedUnexpectedly` 表示至少一个 Process 曾发生无法主动完成结束记录的意外终止，不替代每个 Process 自己的退出原因。

### 悬浮控制条

控制条必须是紧凑、非页面式的顶层工具条；不得退化为带 PageTitle、大段说明和大面积内容区的完整工具页面。

准备：

```text
[开始诊断] [容量上限] [关闭]
```

采集中：

```text
● 诊断中 00:03:21  [标记问题] [截取当前窗口] [结束]
```

完成：

```text
✓ 诊断已保存  [重新开始] [导出] [完成]
```

容量选项在 UI 中使用人类可读文本，例如 `16 MB`、`64 MB`、`256 MB`；Application/Infrastructure 内部继续使用 bytes。

恢复 Active Session 时，控制条必须从 Session snapshot 恢复真实容量、CaptureStopped 状态以及可由持久化数据确定的统计信息，不使用 ViewModel 进程内计数作为跨重启真值。

### Problem Marker 与截图

Problem Marker 表示用户认为问题发生在该时间点附近。Marker 写入、截图、开始/结束、导出等命令统一走可观察的错误边界；异常不得直接逃出 Command。

主动截图只捕获 NovelSpeaker 自身窗口，必须由用户主动触发，不自动截图、不录屏。

### Fatal failure 诊断事实

当活动诊断会话存在时，Process 级 fatal failure 应额外写入一条稳定的结构化 Diagnostic Event，用于说明“何时、从哪个稳定入口发生了致命故障、随后采取了什么动作”。

- Event 字段保持低基数，例如 failure source、severity、shutdown action。
- 不在 `.nsdiag` 中复制 stack trace、异常 Message、资源 Key、路径或用户内容；详细技术证据继续由生产日志负责。
- 即使关联日志暂时不可用，timeline 仍应能够显示发生过 fatal failure。
- 不为这一能力建立第二套 crash store、异常队列或复杂健康状态机。

## 7. 诊断写入与容量

`.nsdiag` 使用 SQLite 作为完整结构化真值。

- Session writer 持续批量落盘。
- hard cap 只代表容量上限；达到上限后停止继续采集并明确提示。
- bounded queue 的瞬时拥塞不得直接等价为永久 `storage-failure`。
- 低价值、高频诊断记录在压力下允许丢弃或聚合，并记录 dropped count。
- Problem Marker、Session lifecycle、CaptureStopped、fatal failure 等关键控制记录应具有高于普通样本的保留优先级。
- 真正的 SQLite/文件写入失败可以使诊断 Session 降级或停止，但不得影响业务。
- Logging 与 `.nsdiag` writer/store 保持独立。

## 8. 问题诊断导出

刚结束 Session 时源 `.nsdiag` 已知，用户只通过保存文件对话框选择最终 ZIP 位置；以后从 Settings 导出旧会话时，需要先选择 `.nsdiag`，再选择 ZIP 保存位置。

设置页“导出问题诊断”的两个文件对话框具有不同目录语义：

1. **选择已有 `.nsdiag` 的 OpenFileDialog**
   - 每次打开都直接进入 NovelSpeaker 的 `Diagnostics` 目录，因为该目录就是应用管理的诊断会话来源；
   - 使用独立、固定的 file-dialog persisted-state profile（例如独立 `ClientGuid`），避免浏览诊断会话时改变其他打开/保存对话框的 Windows 记忆状态；
   - 用户仍可在对话框中主动浏览到其他位置选择兼容的 `.nsdiag`。

2. **选择导出 ZIP 位置的 SaveFileDialog**
   - 不强制进入 `Diagnostics`；
   - 不继承“诊断会话选择器”的初始目录或 persisted-state profile；
   - 继续使用 Windows 对普通保存对话框的既有目录记忆，让用户决定导出包保存位置。

文件对话框的通用 Presentation abstraction 可以支持可选的初始目录和 persisted-state profile，但不得在 Diagnostics ViewModel 中直接创建 Microsoft.Win32 对话框或复制一套平台实现。

默认文件名使用本地时间：

```text
NovelSpeaker-Problem-Diagnostics-yyyyMMdd-HHmmss.zip
```

问题诊断 ZIP 保持：

```text
NovelSpeaker-Problem-Diagnostics-*.zip
├─ summary.md
├─ timeline.md
├─ session.nsdiag
├─ logs.jsonl
├─ environment.json
├─ diagnostics-schema.json
└─ attachments/
```

问题诊断导出是**证据聚合**，不是新的诊断真值 owner：

- `.nsdiag`、生产日志、环境信息各自由原 owner 读取；导出层只负责关联、生成客观摘要并原子写出 Bundle。
- 关联日志读取必须区分“读取成功但没有匹配记录”和“读取不完整/不可用”。单条损坏记录不得导致整个日志文件后续记录被放弃。
- best effort 表示缺少某一证据源仍然可以生成诊断包，不表示静默吞掉退化状态；`summary.md` 必须客观标明相关证据是 complete / partial / unavailable 或等价稳定状态。
- 日志关联优先使用已有 `diagnosticSessionId` / `processInstanceId` 等稳定 correlation，不复制一套异常数据到 Session 只为方便导出。
- 导出代码可以把 Session reader、related-log reader、Bundle writer 等职责拆为小型内部组件，但不得引入通用 evidence pipeline、插件系统或复杂 diagnostics health framework。

普通诊断信息导出与问题诊断导出应共享稳定的 Bundle/atomic-output 基础设施，不分别复制临时文件、ZIP 提交、命名和失败处理逻辑。

## 9. 隐私与非目标

结构化诊断默认禁止用户内容和完整本地路径；详细边界见 `05_DATA_AND_COMPATIBILITY.md`。

第一版仍不做自动上传、在线 backend、Crash Dump/Minidump、自动截图/录屏、Session 列表管理页面、普通遥测 SQLite、复杂 profiler、独立 Crash Database、通用异常路由框架或复杂诊断健康监控。
