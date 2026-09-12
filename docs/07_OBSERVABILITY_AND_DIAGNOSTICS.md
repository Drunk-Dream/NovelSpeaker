# Observability 与诊断

## 1. 定位

NovelSpeaker 使用三套职责清晰的本地诊断能力：

1. **生产日志**：回答“发生了什么异常/失败”。
2. **性能遥测**：回答“长期真实使用中哪里值得优化”。
3. **诊断会话**：回答“这一次可复现问题发生时，应用现场是什么样”。

三者均以本地数据为主，不自动上传。诊断能力不能成为业务故障的新来源。

## 2. 总体架构

业务模块对稳定操作边界只描述一次：

```text
Feature / Application
        ↓
thin Observability API
        ├─ Performance Telemetry
        └─ Diagnostic Session
```

日志保持独立 writer/store，只通过稳定 operation、process、diagnosticSession、activity 等 correlation 与诊断会话关联。

原则：

- 不引入完整 OpenTelemetry 作为第一版基础设施。
- 不建立通用 EventBus。
- Telemetry 与 Diagnostic Session 可以共享基础 operation instrumentation，但各自独立开关、粒度、生命周期和持久化。
- Logging / Telemetry / Diagnostics 任一写入失败不得使业务操作失败。

## 3. 生产日志

### 产品行为

- 默认开启。
- 用户不需要管理日志开关。
- 结构化 JSONL，本地轮转保存。
- 主要记录启动/退出、Warning、Error、Critical、关键恢复/降级与少量重要生命周期。
- 性能慢但成功不自动转为 Warning。

### 日志等级

- **Information**：少量生命周期和大型后台任务等稳定关键事件。
- **Warning**：发生异常/降级但功能继续，例如配置回退、缓存清理、重试后恢复。
- **Error**：用户可感知功能失败，但应用可继续。
- **Critical**：应用无法可靠继续，例如核心初始化失败或未处理异常即将导致退出。

### 长期字段

每条日志保持稳定的核心结构，例如：

- schemaVersion；
- timestamp；
- sequence；
- level；
- eventId / eventName；
- category；
- operation；
- message；
- appVersion；
- processInstanceId；
- 可选 diagnosticSessionId / activityId；
- 可选结构化 properties；
- 可选 exception。

Exception 尽量保留 type、stack trace、HRESULT 与 inner chain；业务上下文保持最小并做隐私清理。

### 写入原则

- 业务线程非阻塞 enqueue。
- bounded queue + 单后台 writer + 批量 append。
- overflow 优先牺牲低价值记录，不阻塞业务。
- dropped count 作为基础设施健康信息记录，不递归写日志。
- 正常退出有限 drain/flush；崩溃 best effort。
- 第一版不实现 Error/Critical Emergency Logger 同步旁路。

## 4. 性能遥测

### 产品行为

- 默认关闭，由用户主动开启。
- 只保存在本地，不自动上传。
- 关闭后立即停止新采集，但已有数据继续保留。
- 用户可以清除历史数据。
- 导出诊断信息在遥测开关关闭时仍可使用。
- Settings 只提供 Toggle、清除、导出和少量说明，不显示监控状态面板。

### 数据模型

普通遥测只保存低开销聚合：

- Counter：次数。
- Histogram：耗时/大小分布。
- Gauge：CPU、Working Set、Managed Heap 等资源摘要。

不保存每次原始普通事件。运行时在内存聚合，定期形成稀疏窗口 JSONL；只有窗口中真正有样本的指标才保存。

Histogram 保存可合并的 count/sum/min/max/fixed buckets；百分位在导出时从合并分布近似计算，不持久化窗口级 P95/P99。

### 第一批关注领域

- app startup/shutdown；
- UI navigation/page critical load/Dispatcher stall；
- Playback start/chapter switch；
- TTS request/retry/failure；
- 少量 Cache/Storage 稳定操作；
- CPU / Working Set / Managed Heap。

指标围绕稳定用户/系统操作边界，不围绕内部方法。标签低基数且由 Registry 声明，禁止 BookId、ChapterIndex、路径、URL、SQL text、用户内容等高基数标签。

### 本地保留

内部策略采用：

- 按日 JSONL；
- 单文件大小分卷；
- 约 30 天保留；
- 总目录约 64 MiB 上限；
- 从最旧文件开始清理。

具体限制是内部策略，不在 UI 展示，也不给用户配置。

## 5. “诊断信息”导出

用于常规优化的诊断信息固定汇总最近一段已有数据，不让用户选择复杂时间范围。

推荐导出 ZIP：

```text
NovelSpeaker-Diagnostics-*.zip
├─ summary.md
├─ telemetry.json
├─ logs.jsonl
└─ environment.json
```

- `summary.md`：客观覆盖时间、版本、数据概况，不自动判断“性能退化原因”。
- `telemetry.json`：指标定义、按版本/日期聚合后的统计和完整 bucket 分布。
- `logs.jsonl`：相关 Warning/Error/Critical 与少量必要生命周期日志。
- `environment.json`：脱敏运行环境摘要。

内部遥测原始窗口格式与导出交换格式分离。

## 6. 诊断会话

### 定位

诊断会话不是“高级性能遥测”，而是生产环境的问题现场收集器。

用户遇到可复现的 UI、状态、播放、缓存、网络、数据库、启动退出或性能问题时，可以主动开始一次诊断会话，复现后结束并交给开发者/AI 分析。

### 生命周期

- 从 Settings 只打开诊断工具，不立即采集。
- 用户点击悬浮控制条“开始诊断”后才创建 Session 和 `.nsdiag`。
- Session Active 后可以跨 NovelSpeaker 正常退出、重启甚至应用升级持续。
- Process 结束不等于 Session 结束。
- 只有用户点击“结束”才将 Session 标记为 Ended。
- 已结束 Session 不重新打开继续写；“重新开始”创建新 Session。
- Active Session marker 只定位权威 `.nsdiag`，不是第二份状态数据库。

### 悬浮控制条

准备：

```text
[开始诊断] [容量上限/次级设置] [关闭]
```

采集中：

```text
● 诊断中 00:03:21
[标记问题] [截取当前窗口] [结束]
```

完成：

```text
✓ 诊断已保存
[重新开始] [导出] [完成]
```

控制条本身就是明显的录制提示，不再增加“诊断时间过长”提示。

### Problem Marker

“标记问题”不是精确故障 timestamp，而表示：

> 用户认为问题发生在该时间点附近。

用户可以在问题准备发生、刚刚发生或发现异常后点击。分析时重点查看 Marker 前后时间窗口。

点击 Marker 可以同时采集轻量状态快照、当前 Activity 列表与资源样本，但不切换复杂“高级采样模式”。

### 主动截图

诊断会话提供可选的“截取当前窗口”：

- 只截 NovelSpeaker 自身窗口。
- 必须由用户主动触发。
- 不自动截图、不连续截图、不录屏。
- 截图与当前 Session/时间点关联。
- 截图可以包含当前界面显示的小说正文、书名等用户内容，这是用户明确主动提供的隐私例外。

### 会话内匿名对象关联

为诊断导航/状态同步问题，允许当前 Session 内建立不可反向映射的匿名对象标识，例如：

```text
Book #1
Chapter #3
```

用途只是判断不同 Activity/Event 是否涉及同一对象：

- 不保存真实 BookId/ChapterId。
- 不保存名称或正文。
- 匿名映射只在当前 Session 内有效。
- Session 结束后不提供真实对象反向映射。

## 7. 诊断会话数据

`.nsdiag` 使用 SQLite 作为完整结构化真值，主要逻辑数据：

- Session / Process lifecycle；
- Activity；
- Event；
- State Snapshot；
- Resource Sample；
- Environment / Configuration；
- Problem Marker；
- 主动截图附件关联。

Activity 描述稳定用户/系统操作边界；Event 描述瞬时关键事实；Snapshot 描述有限领域状态，不允许任意对象 dump。

业务代码不直接操作 SQLite/JSON。Diagnostics Registry 对 Activity/Event/Snapshot definition、字段、枚举和隐私约束进行集中定义。

## 8. 容量与可靠性

诊断会话第一版完整保留当前 Session，不做“旧详细时间线降级为历史 Aggregate”。

- 用户可以在悬浮控制条准备状态选择会话硬容量上限。
- 达到上限后停止继续采集，并明确提示已停止。
- 不实现自动历史淘汰、动态扩容、软阈值降级或按时间自动结束。
- 用户可提高上限后重新复现。
- 默认值和预设大小属于实现策略，可调整，不形成永久产品合同。
- Session writer 持续批量落盘，不能直到结束时才第一次保存。
- 日志和 `.nsdiag` writer/store 独立，避免互相故障耦合。

## 9. 问题诊断导出

诊断结束后 `.nsdiag` 已经是完整可保存的会话文件。用户可以立即导出，也可以以后通过文件选择器选择旧 `.nsdiag` 导出；不建设 Session 列表管理页面。

问题诊断包：

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

- `summary.md`：Session、Process、Problem Marker、异常/降级和容量状态的客观摘要。
- `timeline.md`：面向人的精简时间线，不展开每个资源采样。
- `session.nsdiag`：完整结构化真值。
- `logs.jsonl`：按 diagnosticSessionId 关联的生产日志。
- `environment.json`：脱敏运行环境/白名单配置。
- `diagnostics-schema.json`：由 Registry 自动导出的 Activity/Event/Snapshot 数据字典。
- `attachments/`：用户主动截图等附件。

派生 Markdown/JSON 只在导出时生成，运行时不维护第二份文本真值。

## 10. 隐私与非目标

结构化诊断默认禁止用户内容，详细边界见 `05_DATA_AND_COMPATIBILITY.md`。

第一版明确不做：

- 自动上传；
- 在线 telemetry backend；
- 完整 OpenTelemetry 基础设施；
- Crash Dump / Minidump；
- 自动截图或录屏；
- 问题说明/严重程度/复现步骤表单；
- Session 列表管理页面；
- 普通遥测 SQLite；
- 普通遥测保存每个原始事件；
- 复杂 profiler；
- 诊断运行时同时维护 SQLite + TXT 两份真值。
