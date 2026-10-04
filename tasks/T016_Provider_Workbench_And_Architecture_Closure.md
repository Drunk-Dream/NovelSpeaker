# T016：收敛 Provider 工作台并完成本轮架构验收

## 依赖与阶段性质

依赖 T015。本任务是本轮最终 Closure，必须执行标准完整门禁。

## 目标

让 Speech Provider 工作台复用 T015 中确实通用的编辑/交换/排序/management 行为，同时保留 HTTP 与 Microsoft Edge 的 typed editor、试听和 Voice Catalog 差异；随后对五个阶段目标做一次完整架构审计和清理。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/02_RUNTIME_AND_NAVIGATION.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 9、11 节
- `docs/specs/HTTP_TTS.md`
- `docs/specs/BATCH_MANAGEMENT.md`
- `docs/08_QUALITY_AND_TESTING.md`

## Provider 工作台

- HTTP/Edge draft 保持 typed，不合并为万能 `ProviderConfig`/field dictionary。
- 可复用 editor session 的 open/original/dirty/save/cancel 语义；具体 draft capture、validation、save mapping 留在 provider type owner。
- import/export/copy、stable-key reorder、management selection/batch delete 只复用与 Rules 完全相同的 interaction owner；Provider exchange format 和 workspace persistence 不改变。
- preview/test audio、Voice Catalog/search 是 Provider 专属 operation，不塞入通用 workbench。
- CurrentProvider 仍由 Settings owner 持有；管理页不建立第二份 current truth。

## 最终架构审计

### 变更传播

- UI mutation 调用者不再手工刷新 Playback/Cache/Books；
- Books/Regex 等变化由源模块发布已提交事实，消费者自行解释；
- 没有通用 EventBus/Messenger。

### Playback

- 一个 authoritative runtime；Snapshot 是纯投影；audio callback 通过 session identity 接受；
- 没有 old/new runtime、平行状态或纯转发 wrapper。

### Cache

- App 不理解 internal invalidation aspect/repair protocol；
- 所有页面消费 Cache-owned read model/change source。

### 页面生命周期

- activation 与 latest-operation 分离；没有可由共享 owner 替代的 CTS/version/task registry 样板；
- 真实 data revision 和 background/session owner 保留。

### 工作台

- Rules/Provider 组合小 owner；没有万能泛型 ViewModel、统一业务 DTO 或新的 Shared→Feature 依赖。

## 测试与清理

- 删除迁移 adapter、unused interface、临时 instrumentation/test、过渡注释。
- 核心测试围绕状态 owner、commit boundary、stale result、Cache composition、page lifecycle 和业务差异；删除只锁定旧 orchestration 形状的断言。
- 不修改持久 schema/格式，不扩大产品范围。

## 完整门禁

执行 `AGENTS.md` 标准完整门禁、architecture tests，并检查 `git diff --check`、LF 与无临时产物。若环境限制无法执行，记录真实限制；代码/测试失败不得将 T016 标记完成。

完成后在 `TASK_BACKLOG.md` 汇总五项架构成果、主要净删除、核心测试变化和剩余风险，并删除本文件。
