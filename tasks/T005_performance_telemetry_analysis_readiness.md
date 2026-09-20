# T005 — 提升普通性能遥测的长期可分析性

## 目标

当前普通性能遥测已经能导出宏观 Operation 汇总，但仍有三类结构性不足：

1. CPU / Working Set / Managed Heap 在 `OnOperationCompleted` 中附带采样，采样频率随 Operation 数量变化，空闲时没有资源数据，高操作频率时又重复采样；
2. 内部约一分钟窗口在导出时主要被压缩成按日期/版本汇总，缺少后续分析所需的时间结构和进程生命周期边界；
3. 少数 operation 名称过宽或与实际测量边界不完全一致，无法可靠回答“哪个稳定功能边界变慢”。

本任务不做具体性能优化，也不增加复杂 profiler。目标是让以后导出的普通遥测能够稳定回答：

> 什么时候、哪个版本、哪一次进程运行、哪个稳定功能边界，出现了怎样的性能变化？

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md` 中 T005
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md` 第 4–5 节
- `docs/08_QUALITY_AND_TESTING.md`
- `PerformanceMetricRegistry`
- `OperationCatalog`
- `ObservabilityHub` / `ObservabilityContextAccessor`
- `LocalPerformanceTelemetryStore`
- 所有当前实际使用 `OperationCatalog` 的打点调用点
- 当前普通诊断 ZIP 导出实现及相关 Infrastructure tests

## A. 独立低频进程资源采样

### 目标行为

性能遥测开启期间：

- CPU、Working Set、Managed Heap 采用约 **60 秒一次**的独立低频采样；
- 即使这一分钟没有任何稳定 Operation，也产生资源遥测窗口；
- `OnOperationCompleted` 只负责 Operation count/duration，不再顺带读取进程资源；
- 关闭遥测后立即停止新的周期资源采样；
- 重新开启时重新建立 CPU baseline，禁止把关闭期间 elapsed time 混入下一次 CPU 百分比；
- 应用启动时若设置已经开启遥测，自动进入相同采样生命周期。

### 实现要求

- 复用现有 `TimeProvider`，优先使用可取消、可测试的 timer/periodic owner；
- 不用 `Thread.Sleep` / 固定 `Task.Delay` 驱动长期后台循环；
- timer 有明确 owner，Dispose/Shutdown 有界，不遗留 fire-and-forget 异常；
- 单次采样继续只读取轻量进程/GC 状态，不触发 full GC；
- 采样失败使 Telemetry 自身 degraded/best effort，不使业务失败；
- 60 秒为当前内部策略，不新增 UI 配置项。

## B. 一分钟窗口与进程边界

新写入窗口升级为新的内部 schema，至少包含：

- `windowStartUtc`
- `windowEndUtc`
- `appVersion`
- `processInstanceId`
- 当前窗口实际存在的 metric aggregates

`processInstanceId` 必须复用现有 Observability process context 的匿名 ID；不要在 Telemetry 再生成第二套“进程 ID”。

要求：

- 同一次进程生命周期的窗口使用同一个 process instance；
- 不把 `processInstanceId` 变成 Metric tag；
- 不保存 Windows PID 作为长期关联 ID；
- 旧内部窗口没有该字段时，读取/导出安全降级为缺失/null，不伪造身份，也不因一条 legacy 记录使整个导出失败；
- 不建设内部遥测 migration framework。未发布 JSONL schema 仍是内部实现。

## C. Operation vocabulary 审计

只审计当前已有真实打点，不为了“以后可能有用”新增大量 instrumentation。

原则：

- operation 名称必须描述实际计时边界；
- 如果当前 `storage.query` 实际只包围 SQLite connection open，应改成准确的稳定 operation 语义；只有真正包围 query 执行时才使用 query 语义；
- `cache.operation` 等过宽名称应根据当前真实打点收敛到实际行为，例如当前只覆盖音频生成就使用对应稳定语义；
- 页面 critical load 需要能够区分主要稳定页面/功能表面。优先通过有限、明确的 OperationDefinition 提供上下文，不在本轮为此建设通用任意-tag 系统；
- 允许的页面/功能集合应是低基数稳定枚举，例如 Library / Book Details / Player / Settings / Cache / Rules 这类产品表面；
- 禁止把 Route string、BookId、ChapterId、路径、标题或用户内容写入 Telemetry。

修改 operation ID 时同步 Registry/测试/日志引用，不保留无意义 alias/compat wrapper。

## D. Histogram 分辨率

调整 `operation.duration` 的当前实现 bucket，使同一 Histogram 能较合理覆盖亚毫秒本地操作到数十秒网络/TTS/Cache 操作。

本轮采用以下实现级 bucket（ms）作为起点：

```text
0.1, 0.25, 0.5,
1, 2.5, 5, 10, 25, 50, 100, 250, 500,
1000, 2500, 5000, 10000, 20000, 30000, 60000
```

这组数值是内部策略，不作为 UI/长期外部协议。后续可以根据真实数据调整，但 exporter 必须始终携带实际 `metricDefinitions` / bucket 定义。

## E. `telemetry.json` 导出交换格式

### 1. 导出范围

删除“额外固定 14 天 ExportRange”这一人为限制。

普通诊断导出包含**当前 Telemetry retention/capacity 仍保留的全部窗口**。实际 coverage 由现存数据决定。

内部 retention 继续保持当前约：

- 30 天；
- 64 MiB 总目录上限；
- 现有文件轮转策略。

不新增用户可选时间范围。

### 2. 单文件结构

仍然只输出一个 `telemetry.json`，不新增：

- `telemetry-windows.jsonl`
- `telemetry-schema.json`

新的版本化交换格式至少为：

```text
{
  schemaVersion,
  generatedAtUtc,
  coverage,
  collection,
  metricDefinitions,
  aggregates,
  windows
}
```

#### `coverage`

至少表达：

- 实际最早/最晚窗口；
- 涉及的 app versions；
- process instance 数量；
- 没有窗口时使用明确空值，不制造虚假时间范围。

#### `collection`

至少表达：

- window size；
- 当前 resource sample interval；
- window count；
- dropped window count；
- degraded 状态。

#### `metricDefinitions`

由 `PerformanceMetricRegistry` 生成，不手写第二份 schema。包含：

- name；
- type；
- unit；
- description；
- histogram buckets；
- 允许的有限维度/值域。

#### `aggregates`

保留当前“方便快速阅读/比较”的汇总价值。可继续按 app version + date + metric/operation/outcome 等稳定维度聚合，并带 count/sum/min/max/last 与可用的近似 percentile。

不要只保留 aggregate 而丢掉 windows。

#### `windows`

保留每个约一分钟窗口：

- start/end；
- appVersion；
- processInstanceId；
- 当前窗口的 metric aggregates。

仍然是聚合窗口，不导出每一次原始 Operation event。

### 3. 日志与 summary

- `logs.jsonl` 保持“异常/降级 + 必要生命周期”定位，不增加高频性能日志；
- 日志尽量按此次实际 Telemetry coverage 和现有 process correlation 关联；
- `summary.md` 增加客观 coverage、版本、process/window 数、dropped/degraded 状态；
- summary 不输出“性能变差”“内存泄漏”等自动结论。

## F. 暂不增加的新指标

本轮不要顺手加入：

- GC collection count；
- allocation rate；
- thread count；
- handle count；
- network throughput；
- profiler stack；
- 每个原始 Operation event。

先把现有 CPU / Working Set / Managed Heap / Operation duration/count 的统计意义和时间结构做正确。以后通过真实分析发现缺口再单独规划。

## G. 低开销约束

目标不是“采更多”，而是“以更稳定的采样方式获得更可解释的数据”。

应保持：

- Operation 仍只记录低成本 completion aggregate；
- 资源读取从高频 Operation 路径移除后，高 Operation 频率场景的资源读取次数显著下降；
- 空闲时最多约每分钟一次资源采样/窗口；
- 不在 Dispatcher 上执行无界 JSON/文件处理；
- writer/retention 仍沿用现有 bounded/background 模型。

## 测试策略

遵守已经收敛后的“核心测试优先”规则，不为每个 DTO/字段/常量增加永久测试。

至少保留少量高价值自动回归：

### 1. 资源采样与 Operation 解耦

使用可控 `TimeProvider` / timer seam 验证：

- 没有 Operation 时，遥测开启后仍会产生资源窗口；
- 高频 Operation 不导致每次都额外采 CPU/内存；
- 关闭后停止；
- 重新开启重建 baseline。

不要使用真实 60 秒等待。

### 2. 导出结构与时间信息

一个代表性的 Infrastructure integration test 验证：

- ZIP 可读；
- 单个 `telemetry.json` 同时包含 definitions / aggregates / windows；
- windows 保留不同 appVersion/processInstanceId/time；
- 超过旧 14 天但仍在 retention 内的数据不会仅因固定 ExportRange 被排除；
- legacy 窗口缺少 processInstanceId 时可以安全导出。

不要为 JSON property 顺序、每一个 bucket 或内部 class 名建立单独永久测试。

### 3. Operation vocabulary

只更新已有稳定 observability contract tests 以反映最终 vocabulary；不要为每个调用点增加一条测试。必要的调用点审计可使用临时静态脚本/搜索，完成后删除。

## 自动验收

开发中运行相关 focused tests。

本任务作为本阶段收口，最终执行完整门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

如果环境导致某项无法运行，如实记录；不得通过恢复已删除的细粒度测试、降低隐私边界或移除真实核心测试制造绿色。

## 完成

- 更新 `TASK_BACKLOG.md` 中 T005 为 `[x]` 并记录简短成果；
- 删除本任务规格；
- 删除所有一次性分析脚本、临时测试和导出样本；
- 不自动继续扩展新的 Telemetry 指标或性能优化任务。
