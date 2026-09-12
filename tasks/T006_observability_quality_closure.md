# T006 — Observability 系统收口

## 目标

不增加新产品能力，对 T001–T005 完成的生产日志、性能遥测和诊断会话进行一次跨模块收口：隐私、安全、失败隔离、生命周期、架构边界、旧实现清理和完整质量门禁必须稳定。

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md` 中 T006
- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- T001–T005 当前实现及相关测试

## 必须完成的审计

### 1. 隐私

验证所有结构化入口：

- Production Log properties/exception sanitization；
- Telemetry tags；
- Diagnostic Activity/Event/Snapshot；
- environment/config white list；
- session anonymous association；
- export；
- screenshot explicit exception。

禁止用户内容通过“临时调试字段”“方便排错”等旁路进入结构化数据。

### 2. 失败隔离

通过可控 fake/fault injection 验证：

- Log queue/store 失败；
- Telemetry writer/rotation/export 失败；
- `.nsdiag` create/write/checkpoint/migration 失败；
- Diagnostic export 失败；
- screenshot capture 失败。

这些故障不得把原本成功的播放、导航、缓存、设置等业务改成失败，也不得导致无限重试/递归日志。

### 3. 生命周期

核对：

- process start/shutdown；
- telemetry toggle；
- active diagnostic session 跨重启；
- session End；
- restart 新 session；
- app version 跨 session process；
- Settings/Window/Tray 生命周期；
- shutdown 有界。

不得因为 Diagnostics owner 引入新的 singleton Page/ViewModel 或生命周期循环。

### 4. 架构与重复实现

删除本轮遗留：

- parallel old logger；
- compat wrapper；
- alias interfaces；
- 未使用 Registry definition；
- 临时 adapters；
- orphan DI registration；
- 一次性 debug UI/script/screenshot；
- task-specific feature flag。

确保：

- shared instrumentation 不演化成通用 EventBus；
- Logging/Telemetry/Diagnostics stores 独立；
- 业务层不依赖 JSONL/SQLite/ZIP/WPF capture 实现；
- App Feature/Application 模块无新循环。

### 5. 数据与容量

验证：

- 日志和遥测 rotation/retention；
- telemetry 清理；
- `.nsdiag` hard cap；
- active marker stale/corrupt；
- ended session 可独立读取；
- exports 不依赖运行时未 checkpoint 的临时文件。

### 6. 文档一致性

长期文档已经在本轮开发前定义目标终态。

本任务只做一致性检查：

- 如果实现与文档仅有类名/参数等实现差异，不修改长期文档。
- 如果发现长期目标在真实源码约束下根本不可成立，记录为 `[!]` 阻塞/新规划输入，不自行降低产品、架构或隐私要求。
- 不创建新的 Decisions/Archive/implementation-report 长期文档。

## 自动验收

至少执行：

- 所有 observability/logging/telemetry/diagnostics focused tests；
- Architecture Fitness Tests；
- Presentation/WPF 隔离测试；
- privacy/schema registry tests；
- failure-injection tests；
- export round-trip/readability tests。

然后完整门禁：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
```

不要新增绝对毫秒级 CI 性能阈值。可以做结构性/数量级 smoke checks，但不能用脆弱 timing gate 获取“性能绿色”。

人工验收始终可选，不阻塞 T006 完成。

## 完成

1. `TASK_BACKLOG.md` 将 T006 标记 `[x]` 并记录收口成果和最终自动门禁结果。
2. 删除本文件。
3. `tasks/` 应不再保留本 Phase 已完成的实施规格。
4. 不自动规划下一阶段功能。
