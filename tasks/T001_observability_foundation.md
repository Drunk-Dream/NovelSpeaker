# T001 — Observability 基础合同

## 目标

建立一层足够薄、长期稳定的 Observability 基础，使业务模块只描述一次稳定操作语义，后续性能遥测与诊断会话分别消费，而不是在业务代码中形成两套平行埋点。

本任务只建立合同、Registry、上下文传播和自动架构保护，不实现生产日志文件、普通遥测持久化或 `.nsdiag`。

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md` 中 T001
- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- 相关现有 Application/Infrastructure/App 代码和测试

以当前源码为实现基线。类名和目录可按现状调整，但不得改变长期架构边界。

## 必须实现

### 1. 薄 Observability API

建立业务可使用的窄合同，用于表达：

- 稳定 Operation/Activity 边界；
- 操作结果；
- 有限 Diagnostic Event；
- 后续 Telemetry/Diagnostic consumer 所需的最小上下文。

要求：

- 没有 consumer 启用时应接近 no-op，不产生文件 I/O。
- 业务模块不得知道 Telemetry JSONL、SQLite `.nsdiag`、导出 ZIP 等存储细节。
- 不以 `Dictionary<string, object>` 或自由字符串作为业务侧主要 API。
- 不引入完整 OpenTelemetry。
- 不建立 EventBus/Messenger/CommandBus。

### 2. Stable Operation Catalog

集中定义长期稳定的业务 Operation 词汇，例如播放开始、章节切换、页面导航、TTS 请求、Cache 关键操作和 Storage 逻辑查询。

Operation 名称描述稳定业务语义，不绑定具体 ViewModel/Service/方法名。

后续日志 Event Registry 可以引用同一 Operation 词汇，但日志 EventId 与 Diagnostic DefinitionId 必须保持独立。

### 3. Diagnostic Registry 基础

为后续诊断会话建立可枚举、强类型的 Activity/Event/Snapshot definition 基础：

- definition id 唯一且稳定；
- 声明 domain、description、字段类型和允许枚举；
- 字段白名单；
- 隐私分类/验证基础；
- definition 含义发布后不得复用为不同含义；
- 允许 Deprecated，但历史含义必须可解释。

本任务不需要一次性定义所有未来诊断点，只建立机制和第一批共享定义。

### 4. Correlation Context

建立极小的上下文传播能力，使后续系统可以关联：

- process instance；
- diagnostic session（未来）；
- current activity（未来）。

不得要求业务代码层层手动传递存储层 ID。异步上下文传播必须有明确生命周期，不能造成跨无关操作泄漏。

### 5. Consumer 分离

设计允许至少两个独立 consumer：

- ordinary performance telemetry；
- diagnostic session。

两者可以消费同一个 operation instrumentation，但生命周期、开关、粒度和持久化必须独立。

日志只需要能够读取 correlation/context，不与两个 consumer 共用 writer/store。

## 不在本任务范围

- 改写现有生产日志持久化；
- Telemetry JSONL；
- Settings 遥测 UI；
- `.nsdiag` SQLite；
- Diagnostic Session 生命周期；
- 浮动控制条；
- Problem Marker；
- 截图；
- 诊断 ZIP；
- OpenTelemetry adapter；
- Crash Dump。

## 架构约束

- 合同优先放在正确的 Application/Domain 边界，技术实现放 Infrastructure；不要因为“Observability”而打破四层结构。
- App Feature 不直接依赖 Infrastructure observability store。
- 不新增独立 assembly。
- 不让 Shared 成为跨模块依赖垃圾桶。
- 不要求现有所有业务路径在本任务全部接入；只建立可复用基础并在少量代表路径验证。

## 自动验收

必须新增/调整自动测试，至少验证：

1. Operation/Definition ID 唯一和命名规则。
2. Definition 字段来自声明合同，禁止未声明的自由字段。
3. 明确禁止的用户内容字段不能注册为结构化诊断字段。
4. 无 consumer 时 API 安全 no-op。
5. Consumer 隔离：一个 consumer 失败不能阻断另一个或业务调用。
6. correlation context 不在无关异步操作间泄漏。
7. Architecture Fitness Tests 继续通过，无新层级/模块环。

运行相关 focused tests，随后执行完整门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

人工验收仅可选，不阻塞任务。

## 完成

自动验收通过后：

1. 在 `TASK_BACKLOG.md` 将 T001 标记 `[x]`，记录简短完成成果。
2. 删除本文件。
3. 不等待人工确认即可继续 T002。
