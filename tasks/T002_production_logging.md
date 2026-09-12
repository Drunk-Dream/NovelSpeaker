# T002 — 生产结构化日志

## 目标

在现有生产日志能力上完成结构化、低开销、可长期维护的本地生产日志系统。**当前仓库已经存在 production logging / centralized error boundary 相关实现，本任务必须先审计并复用或重构现有能力，不得平行再造第二套日志系统。**

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md` 中 T002
- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- T001 已完成后的 Observability contracts
- 当前所有 logging/error-boundary 生产代码和测试

## 必须实现

### 1. 现有日志整合

先定位当前 rolling/redacted production logging、全局错误边界、错误对话框和相关 DI。

目标是收敛成一套生产日志路径：

- 默认开启；
- JSONL；
- stable EventId / EventName / Category / Operation；
- 不再保留功能重叠的旧 logger/writer/compat wrapper。

不要为了匹配本文类名而机械重写职责已经正确的现有实现。

### 2. 稳定日志 Schema

每条记录至少具有稳定核心语义：

- schemaVersion；
- UTC timestamp；
- process 内 sequence；
- level；
- eventId / eventName；
- category；
- operation；
- short rendered message；
- appVersion；
- processInstanceId；
- 可选 diagnosticSessionId / activity correlation；
- 可选 structured properties；
- 可选 structured exception。

无值的可选字段可以省略。

### 3. Event Registry

集中维护日志 Event 定义：

- EventId 唯一，发布后不复用；
- 稳定 EventName；
- stable Operation Catalog 引用；
- Default level；
- Category；
- Description。

不要让业务代码散落魔法数字和任意 event 字符串。

### 4. Exception 记录与隐私

Exception 需要尽量保留：

- type；
- HRESULT/平台错误码；
- stack trace；
- inner exception tree/chain；
- message（统一做必要 sanitization）。

必须避免把小说正文、书名、章节标题、完整路径、URL/query、Header/Body、Token、TTS 文本、Regex 正文、SQL 用户参数和缓存音频写入日志。

不要自行把性能耗时阈值转换成 Warning。

### 5. 非阻塞写入与轮转

生产调用路径不得同步等待磁盘落盘。

目标行为：

- bounded queue；
- 单后台 writer；
- 小批量 append；
- 按日期和大小轮转；
- retention + 总容量保护；
- 正常退出有限 drain/flush；
- 崩溃 best effort；
- queue overflow 优先牺牲低价值日志并统计 dropped records；
- writer/磁盘失败进入 degraded，而不是使业务失败。

具体队列长度、batch、flush 周期、文件上限是实现参数，自行选择并通过测试覆盖，不进入 UI。

### 6. 与诊断上下文关联

当将来存在 active diagnostic session/activity 时，日志自动附加可用 correlation；没有诊断会话时日志仍正常工作。

日志 writer/store 与 `.nsdiag` writer/store 必须独立。

## 不在本任务范围

- Error/Critical 同步 Emergency Logger 旁路；
- 远程上传；
- 日志数据库；
- Debug/Trace 生产长期日志；
- 性能遥测；
- `.nsdiag`；
- 自动性能判断。

## 自动验收

至少覆盖：

1. JSONL schema 与 stable Event registry。
2. 日志轮转/retention 基础行为。
3. queue overflow 不阻塞业务，并能反映 dropped 记录。
4. writer 模拟失败时业务路径仍成功/按原业务语义失败，而不是被 logger 改写。
5. 正常 shutdown 有界 drain。
6. Exception chain、stack trace、HRESULT 的结构化输出。
7. sanitization / privacy regression tests。
8. diagnostic correlation presence/absence 正确。
9. 现有 error-boundary 行为保持，用户错误展示不泄露技术异常。
10. 不存在两套并行 production rolling logger。

运行 focused tests 和完整门禁。人工验收仅可选。

## 完成

自动验收通过后更新 T002 完成成果并删除本文件，不等待人工验收即可进入 T003/T004。
