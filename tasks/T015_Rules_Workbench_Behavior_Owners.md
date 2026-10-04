# T015：提取 Rules 编辑工作台的明确行为 owner

## 依赖与阶段性质

依赖 T014。任务结束时仓库必须保持可构建、相关 focused tests 通过。

## 目标

在不统一 Chapter/Regex/Metadata 业务模型的前提下，把三个规则工作台重复的编辑生命周期、import/export interaction、reorder 与批量管理 orchestration 收敛为可组合的小型行为 owner，使 ViewModel 回到业务字段投影和命令连接。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 3、4、5 节
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 3 节
- `docs/specs/BATCH_MANAGEMENT.md`
- `docs/specs/REGEX_REPLACEMENT.md`（Regex 相关行为）

## 以现有设施为基础

优先复用/完善：

- `EditorSession<TId,TEditor>`：original/draft/dirty/new/cancel；
- `RuleImportSession`：导入 operation lifecycle；
- `RuleReorderController`：stable-key reorder 纯计算；
- `ManagementSelectionController<TKey>`：management selection。

只有当多个 ViewModel 中存在相同的完整行为链时才增加新的小 owner，例如 exchange interaction 或 batch-delete session。新的 owner 不继承 `ObservableObject`，不拥有业务 repository/workspace，不知道具体规则字段。

## 迁移范围

- Chapter Rules 与 Regex Rules 优先，因为两者已有相近 session/命令结构。
- Metadata workbench 复用真正相同的行为，但保留其 typed read/write/remove/order 抽象和自身 validation。
- 导入解析、内建 Chapter Rule 保护、Regex scope/timeout、Metadata 专属格式继续由各业务 workspace/codec 拥有。
- ViewModel 仍负责面向用户的文案、具体确认内容、feedback 和 typed draft 映射。

## 禁止方案

- `GenericRuleWorkbenchViewModel<T...>` 或大型基类；
- 用反射/动态字典统一 editor fields；
- 统一 Chapter/Regex/Metadata JSON schema 或 repository；
- 为消除少量重复建立多层 callback 泛型；
- 把 navigation/dialog/clipboard/file 全部塞入万能 manager。

## 减法与测试

- 删除各 ViewModel 中重复的 CTS、dirty bookkeeping、selection sync、reorder index 算法和 import busy 状态，仅限已有 owner 可完整替代的部分。
- shared owner 用少量行为测试覆盖；各页面只保留其业务差异和关键交互测试，不复制 owner 全套测试。
- 保持 unsaved-change guard、normal selection 与 management selection 分离、batch partial continuation、内建规则限制和导入反馈。

## 强制验证

- Chapter/Regex/Metadata create/edit/save/cancel/delete/reorder/import/export/copy 与 management mode 核心行为保持。
- failed/cancelled import 不污染 editor/selection；reorder failure 可恢复 projection。
- Shared/Rules Shared 不依赖具体 Feature 或业务 repository。
- 对比修改前后，确有净删除重复 orchestration；运行三类规则 focused tests、format、Release build。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
