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

## 4. 性能遥测

### 产品行为

- 默认关闭，由用户主动开启。
- 只保存在本地，不自动上传。
- 关闭后立即停止新采集，但已有数据继续保留。
- 用户可以清除历史数据。
- 导出诊断信息在遥测关闭时仍可使用。
- Settings 只提供 Toggle、清除、导出和少量说明，不显示监控状态面板。

### 数据模型

普通遥测保存 Counter、Histogram 与 Gauge 的低开销聚合，不保存每次原始普通事件。运行时形成稀疏窗口 JSONL，只保存真正有样本的指标。

指标围绕稳定用户/系统操作边界，不围绕内部方法。`UiDispatcherStall` 只代表具有“等待/阻塞异常”语义的调度器观测，不得把每一次普通 Dispatcher 调用都记录成 stall。

标签低基数且由 Registry 声明，禁止 BookId、ChapterIndex、路径、URL、SQL text、用户内容等高基数标签。

### 本地保留

- 按日 JSONL；
- 单文件大小分卷；
- 约 30 天保留；
- 总目录约 64 MiB 上限；
- 从最旧文件开始清理。

## 5. “诊断信息”导出

用于常规优化的诊断信息固定汇总已有数据，不让用户选择复杂时间范围。

用户点击导出后打开 Windows **保存文件对话框**，只用于选择最终 ZIP 的保存位置和文件名；应用自行读取内部 Telemetry/Logs 数据，不要求用户选择内部诊断文件或目录。

默认文件名使用本地时间：

```text
NovelSpeaker-Diagnostics-yyyyMMdd-HHmmss.zip
```

ZIP：

```text
NovelSpeaker-Diagnostics-*.zip
├─ summary.md
├─ telemetry.json
├─ logs.jsonl
└─ environment.json
```

导出可靠性要求：

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

## 7. 诊断写入与容量

`.nsdiag` 使用 SQLite 作为完整结构化真值。

- Session writer 持续批量落盘。
- hard cap 只代表容量上限；达到上限后停止继续采集并明确提示。
- bounded queue 的瞬时拥塞不得直接等价为永久 `storage-failure`。
- 低价值、高频诊断记录在压力下允许丢弃或聚合，并记录 dropped count。
- Problem Marker、Session lifecycle、CaptureStopped 等关键控制记录应具有高于普通样本的保留优先级。
- 真正的 SQLite/文件写入失败可以使诊断 Session 降级或停止，但不得影响业务。
- Logging 与 `.nsdiag` writer/store 保持独立。

## 8. 问题诊断导出

刚结束 Session 时源 `.nsdiag` 已知，用户只通过保存文件对话框选择最终 ZIP 位置；以后从 Settings 导出旧会话时，先选择 `.nsdiag`，再选择 ZIP 保存位置。

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

普通诊断信息导出与问题诊断导出应共享稳定的 Bundle/atomic-output 基础设施，不分别复制临时文件、ZIP 提交、命名和失败处理逻辑。

## 9. 隐私与非目标

结构化诊断默认禁止用户内容和完整本地路径；详细边界见 `05_DATA_AND_COMPATIBILITY.md`。

第一版仍不做自动上传、在线 backend、Crash Dump/Minidump、自动截图/录屏、Session 列表管理页面、普通遥测 SQLite 或复杂 profiler。
