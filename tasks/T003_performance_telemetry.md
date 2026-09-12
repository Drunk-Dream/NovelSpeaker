# T003 — 普通性能遥测与诊断信息导出

## 目标

实现默认关闭、用户主动开启、仅本地保存的普通性能遥测，以及面向日常优化的“诊断信息”导出。普通遥测用于长期真实使用趋势，不是 profiler，也不保存每次原始事件。

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md` 中 T003
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- T001/T002 完成后的代码

## 必须实现

### 1. 设置与产品行为

Settings 中使用一行轻量入口：

- 性能遥测 Toggle；
- 清除遥测数据的 Icon Button；
- 导出诊断信息的 Icon Button；
- 简短说明“仅本地保存，不自动上传”。

不要增加：

- 已保存大小面板；
- 最近采集时间；
- 运行状态 Dashboard；
- retention 配置；
- 用户选择导出时间范围。

关闭 Toggle：

- 立即停止新的普通性能采集；
- 已有数据继续保留；
- 不影响生产日志；
- 不影响未来诊断会话；
- “导出诊断信息”仍可使用。

### 2. Metric Registry 与第一批指标

在 T001 shared instrumentation 上实现普通遥测 consumer。

指标只使用 Counter / Histogram / Gauge，集中 Registry 定义 name/type/unit/buckets/allowed tags/description。

第一版覆盖稳定高价值领域：

- app startup / shutdown；
- UI navigation / page critical load / Dispatcher stall；
- playback start / chapter switch；
- TTS request / retry / failure；
- 少量稳定 Cache / Storage operation；
- process CPU / Working Set / Managed Heap。

具体指标数量与 buckets 自行收敛，避免高基数标签和内部方法级指标。

### 3. 内存聚合与 JSONL

- 普通业务样本在内存聚合。
- 周期形成稀疏窗口。
- 只有有样本/有采样的指标写入。
- Histogram 保存 count/sum/min/max/fixed buckets。
- 不保存每个原始普通事件。
- 不在窗口持久化 P50/P95/P99。
- Gauge 只保存窗口摘要。
- JSONL 按日期并按大小分卷。
- 每个窗口带 appVersion/schemaVersion，窗口不跨 appVersion。

内部 retention 初始策略：

- 约 30 天；
- Telemetry 目录总容量约 64 MiB；
- 单文件约 8 MiB 分卷；
- 超限从最旧开始清理。

这些值是内部策略，可集中配置，不进入 UI/长期用户合同。

### 4. 诊断信息导出

固定汇总最近约 14 天已有数据，不要求用户选时间范围。

生成 ZIP：

```text
NovelSpeaker-Diagnostics-*.zip
├─ summary.md
├─ telemetry.json
├─ logs.jsonl
└─ environment.json
```

要求：

- `summary.md` 只陈述客观事实，不自动诊断“某版本更差”。
- `telemetry.json` 从窗口合并生成，按日期/版本提供统计与完整 bucket 分布。
- percentile 只在合并后计算近似值，禁止平均窗口 P95。
- `logs.jsonl` 包含相关 Warning/Error/Critical 和极少量必要生命周期日志。
- `environment.json` 使用白名单、脱敏环境信息。
- 不把内部原始窗口文件直接复制到 ZIP。
- 不混入未来 `.nsdiag` 会话。

### 5. 失败隔离

Telemetry writer/export 失败不得影响正常播放、导航、缓存等业务。

普通遥测允许损失最后一个短聚合窗口，不为它实现复杂事务可靠性。

## 不在本任务范围

- 普通遥测 SQLite；
- 原始事件时间线；
- 远程上传；
- OpenTelemetry backend；
- 自动版本优劣判断；
- Dashboard；
- 诊断会话；
- Session 截图。

## 自动验收

至少覆盖：

1. 默认关闭。
2. 开关关闭后不再产生新样本，但历史仍可导出/清除。
3. 清除不改变 Toggle 状态且不删除生产日志。
4. Sparse window 与 histogram 合并正确。
5. percentiles 不通过“平均 percentile”计算。
6. retention/rotation 有界。
7. high-cardinality / forbidden tags 被 Registry 拒绝。
8. 导出 ZIP 结构和内容稳定。
9. 关闭 telemetry 时导出仍可成功。
10. telemetry writer/export 失败不改变业务结果。
11. Settings UI 不出现数据状态 Dashboard。

运行 focused tests 和完整门禁。WPF/视觉人工验收仅可选，不阻塞完成。

## 完成

自动验收通过后更新 T003 完成成果并删除本文件。
