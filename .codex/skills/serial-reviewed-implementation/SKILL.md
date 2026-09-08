---
name: serial-reviewed-implementation
description: 严格串行完成代码任务切片：主 agent 负责实现、动态验证、修复和提交；每个任务切片创建一个独立 review subagent，按本 Skill 的静态审查准则持续复审当前切片的最新完整 diff，直到返回 PASS。若需要修改，由主 agent 修复并重新验证；若需要补充动态证据，由主 agent 执行针对性验证；同一切片持续复用同一个 review subagent，审查通过后关闭并进行原子提交。适用于需要“实现—验证—独立静态审查—修复/补充验证—复审—提交”流程的 Git 代码项目；不适用于纯代码审查或明确禁止提交的任务。用户明确要求使用本 skill 时，即视为已授权本 skill 所执行任务切片的原子提交。
---

# 串行实现、独立 Review 与原子提交

本 Skill 面向 Codex 代码修改工作流设计，适用于不同项目、语言、构建系统和测试框架。

核心目标：

- 主 agent 专注实现、动态验证、修复和提交；
- review subagent 专注独立静态代码审查；
- 每个任务切片拥有一个持续存在的 review subagent；
- review subagent 的详细代码阅读和审查推理保留在其独立上下文中；
- 主 agent 只接收完成决策所需的结论、findings 和验证请求；
- 当前切片的最终完整 diff 通过 Review 后再提交。

## 1. 总体流程

每个任务切片严格串行执行：

```text
主 agent
  ├─ 读取项目规则和任务要求
  ├─ 定义任务切片
  ├─ 实现
  ├─ 执行必要动态验证
  ├─ 确认当前切片完整 Review 范围
  │
  └─ 创建该切片专属 review subagent
        ├─ 读取 references/review-guidelines.md
        ├─ 静态审查当前切片最新完整 diff
        ├─ PASS
        │    └─ 关闭 subagent → 原子提交
        ├─ CHANGES_REQUIRED
        │    └─ 主 agent 修复 + 验证
        │         └─ 同一 subagent 完整复审最新 diff
        ├─ VERIFICATION_REQUIRED
        │    └─ 主 agent 执行针对性验证
        │         └─ 同一 subagent 结合新证据完整复审
        └─ REVIEW_INVALID
             └─ 修正 Review 输入或环境
                  └─ 同一 subagent 可继续时继续复审
```

完成当前切片后，再开始下一切片，并为下一切片创建新的 review subagent。

## 2. 角色职责

### 主 agent

主 agent 负责：

- 理解任务；
- 读取项目规则；
- 划分任务切片；
- 修改源码；
- 补充或修改测试；
- 执行动态验证；
- 判断并处理 Review findings；
- 执行 Review 请求的补充验证；
- 维护当前切片 Review 范围；
- 在 Review 通过后组织原子提交；
- 在切片完成时关闭 review subagent。

动态验证统一由主 agent 执行，包括项目适用的：

- build；
- format；
- lint；
- typecheck；
- unit test；
- integration test；
- benchmark；
- static analyzer 的可执行检查；
- 应用或服务运行验证；
- 项目规定的其他动态检查。

### review subagent

review subagent 负责：

- 读取 `references/review-guidelines.md`；
- 读取适用于当前改动的项目规则；
- 检查当前切片最新完整 diff；
- 阅读必要的 surrounding code；
- 查找相关调用点、引用、接口和数据流；
- 阅读相关测试代码；
- 检查实现与项目架构、约定和接口契约的一致性；
- 识别当前切片引入的具体缺陷和回归风险；
- 必要时提出针对性的动态验证请求；
- 每次复审都重新检查最新完整 diff；
- 向主 agent 返回精炼的 Review 结果。

review subagent 使用只读方式完成代码审查。动态验证结果由主 agent 提供，subagent 将这些结果作为已有证据使用。

## 3. 前置检查

开始任务前，主 agent：

1. 确认当前目录位于 Git 仓库中。
2. 读取适用的项目说明和开发约束，例如：`AGENTS.md`、`AGENTS.override.md`、`CONTRIBUTING`、README、项目 docs、CI 配置、构建配置和当前任务要求。
3. 记录初始 `git status`。
4. 识别任务开始前已存在的 staged、unstaged 和 untracked 修改。
5. 保护这些既有修改，使当前任务切片能够独立理解、Review 和提交。
6. 将任务拆分为边界清晰的任务切片。

任务足够小时，可以只使用一个切片。

## 4. 提交授权

用户明确要求使用本 Skill，即视为已授权本 Skill 所执行任务切片的原子提交，提交前无需再次请求确认。

该授权覆盖本 Skill 流程产生的本地 commits。

以下远端操作仍需要用户单独明确授权：push、tag、Release、创建或合并 PR、修改远端分支以及其他远端写操作。

## 5. 定义任务切片

修改代码前，主 agent 明确：

- 当前切片目标；
- 范围和不包含的内容；
- 验收标准；
- 预计涉及的文件或模块；
- 适用的动态验证；
- Review 基准；
- 当前切片的预期 diff 范围；
- 任务开始前已有且不属于当前切片的修改。

切片应足够小，使其 diff 可以独立理解、验证、Review 和提交。

实施过程中如果范围发生实质变化，先更新当前切片定义，再继续。

## 6. 实现

当前切片的源码修改由主 agent 完成。

主 agent：

- 遵循项目现有架构、接口、风格和约定；
- 聚焦当前切片所需的修改；
- 补充必要测试；
- 优先复用项目已有成熟库、框架、组件和基础设施；
- 持续检查工作区，保持当前切片边界清晰。

## 7. 动态验证

实现完成后，主 agent 根据项目实际情况执行必要验证。

验证范围以项目规范、CI 要求、修改风险和当前切片验收标准为依据。

记录关键验证命令和结果。发现问题后，主 agent 修复并重新执行受影响验证。

进入 Review 前确认：

- 当前实现满足验收标准；
- 必要动态验证已经完成；
- `git status` 已检查；
- 当前切片完整 diff 已检查；
- 当前切片新增文件已纳入范围；
- Review 基准和范围明确；
- 既有无关修改与当前切片可以可靠区分。

## 8. 准备 Review 范围

Review 的目标是：

> 当前任务切片相对于既定 Review 基准的最新完整改动。

如果当前工作区只包含当前切片修改，可以直接使用当前工作区。

如果工作区混有其他既有修改，主 agent 应准备一个能够准确表示当前切片的 Review 范围。Git 项目优先使用临时 worktree 或等价隔离 workspace，使该 workspace 相对于 Review 基准的全部变化恰好等于当前切片。

向 review subagent 提供：

- Review workspace；
- Review 基准；
- 当前切片目标；
- 验收标准摘要；
- 当前切片范围；
- 已完成的动态验证及结果；
- 需要排除的既有修改；
- 必要的设计约束。

## 9. 创建当前切片的 review subagent

完成当前切片首次实现和动态验证后，主 agent 创建一个专属于该切片的 review subagent。

该 subagent：

1. 读取 `references/review-guidelines.md`。
2. 读取适用于 changed files 的项目规则。
3. 获取当前切片最新完整 diff。
4. 执行独立静态代码审查。
5. 返回 Review 结果。
6. 在当前切片后续复审中持续复用。

主 agent 将动态验证摘要提供给 subagent，例如：

```text
Validation completed:
- <command>: PASS
- <command>: PASS
```

这些结果作为已经完成的动态证据供 Review 使用。

## 10. Review 结果协议

review subagent 返回以下四种结果之一。

### `PASS`

表示：

- 已检查当前切片最新完整 diff；
- 没有未解决的 actionable finding；
- 没有仍需补充的关键动态验证；
- 当前 patch 可以进入提交阶段。

### `CHANGES_REQUIRED`

表示发现一个或多个应在提交前修复的具体代码问题。

每个 finding 应包含：

- 优先级，如可判断；
- 文件和最短有用位置；
- 问题描述；
- 触发场景或受影响路径；
- 实际影响；
- 必要时给出简洁修复方向。

### `VERIFICATION_REQUIRED`

表示当前静态证据不足以确认某个重要问题，但可以通过针对性的动态验证获得结论。

验证请求应说明：

- 需要验证什么；
- 为什么需要验证；
- 涉及的代码路径或场景；
- 最小且最有针对性的验证方式。

### `REVIEW_INVALID`

表示本轮无法形成可信 Review，例如当前完整 diff 无法确定、必要文件或项目规则无法读取、Review 范围在审查过程中发生变化，或 subagent 无法完成当前审查。

应同时说明恢复 Review 所需的信息或动作。

## 11. 处理 `CHANGES_REQUIRED`

主 agent：

1. 逐项核实 finding。
2. 修复成立的问题。
3. 对未采纳 finding 准备具体代码、接口、设计或验证依据。
4. 重新执行受修改影响的必要动态验证。
5. 再次检查当前切片最新完整 diff。
6. 将最新 diff 状态、验证结果和 finding 处理结果交回**同一个 review subagent**。
7. 要求其按照 `references/review-guidelines.md` 重新执行完整 Review。

同一 review subagent 的上一轮上下文用于理解历史 findings，但每轮复审都以最新完整 diff 为审查对象，并继续寻找新的问题。

重复此循环直到 `PASS`。

## 12. 处理 `VERIFICATION_REQUIRED`

主 agent：

1. 判断验证请求是否与当前切片相关且必要。
2. 执行最小、针对性的动态验证。
3. 记录命令和结果。
4. 如果验证暴露实现问题，修复代码并重新执行受影响验证。
5. 将新的动态证据和最新完整 diff 状态交回**同一个 review subagent**。
6. 要求其重新执行完整 Review。

review subagent 根据新的动态证据更新判断，同时继续审查最新完整 diff。

## 13. 处理未采纳 finding

主 agent 对未采纳 finding 提供具体依据，例如：相关接口契约、调用方约束、生命周期保证、项目设计说明、已有测试、新执行的验证结果或其他可检查事实。

将这些依据交回同一 review subagent。

review subagent 重新检查原 finding 是否仍成立、提供的依据是否充分，以及最新完整 diff 是否存在其他问题。

## 14. 每轮复审要求

同一个 review subagent 在每次复审时都执行：

1. 重新读取当前切片最新完整 diff。
2. 检查上一轮 findings 的处理结果。
3. 重新评估所有 changed behavior。
4. 检查修复是否引入新问题。
5. 继续搜索其他 actionable findings。
6. 检查新增动态验证是否解决对应不确定性。
7. 只有最新完整 diff 不再存在未解决问题和必要验证时才返回 `PASS`。

复审的范围始终是最新完整 diff，而不是上一轮 findings 的局部补丁。

## 15. Review subagent 生命周期

一个任务切片对应一个 review subagent。

```text
创建
  ↓
首次完整 Review
  ↓
CHANGES_REQUIRED / VERIFICATION_REQUIRED / 可恢复的 REVIEW_INVALID
  ↓
主 agent 处理
  ↓
同一 subagent 完整复审
  ↓
...
  ↓
PASS
  ↓
关闭 subagent
```

如果当前 subagent 无法继续工作，则为当前切片创建新的 review subagent。新的 subagent 重新读取 `references/review-guidelines.md`，并从当前最新完整 diff 开始独立 Review。

下一任务切片始终创建新的 review subagent。

## 16. `PASS` 后的修改

`PASS` 对其审查的最新完整 diff 有效。

如果 `PASS` 后发生可能影响代码行为、接口、配置、数据格式、依赖关系、构建逻辑、测试语义或生成结果的修改，主 agent：

1. 重新执行受影响的动态验证。
2. 将最新完整 diff 和验证结果交回当前 review subagent。
3. 由同一 subagent 再次完整 Review。
4. 获得新的 `PASS`。

## 17. 原子提交

获得最终 `PASS` 后：

1. 主 agent 再次检查 `git status`。
2. 再次检查完整 diff。
3. 确认提交内容与最终 Review 内容一致。
4. 确认既有无关修改保持原状。
5. 关闭当前 review subagent。
6. 按逻辑组织一个或多个原子 commit。

原子提交要求：

- 每个 commit 有一个明确目的；
- 每个 commit 的改动逻辑完整；
- 不同性质改动尽量分开；
- commit message 简洁准确；
- 每个 commit 可以独立理解。

如果 staging、commit hook、formatter、codegen 或其他提交准备步骤改变了文件内容：

1. 重新执行必要动态验证。
2. 重新检查完整 diff。
3. 恢复或重新创建当前切片的 review subagent。
4. 对最新完整 diff 执行完整 Review。
5. 获得新的 `PASS` 后完成提交。

## 18. 完成条件

当前切片完成需要同时满足：

- 实现满足验收标准；
- 必要动态验证通过；
- review subagent 对最终完整 diff 返回 `PASS`；
- 所有成立 findings 已处理；
- 所有必要动态验证请求已处理；
- 提交内容与最终 Review 内容一致；
- 当前切片修改已按原子提交原则提交；
- review subagent 已关闭；
- 当前切片不存在未提交修改；
- 任务开始前的无关修改保持原状。

然后开始下一切片。

## 19. 任务完成

全部切片完成后，汇总：

- 已完成的任务切片；
- 每个切片的关键动态验证；
- 每个切片最终 Review 结果；
- 主要 findings 及处理结果；
- 补充验证及结果；
- 创建的 commits 及其目的；
- 仍存在但不属于本任务范围的既有修改或风险。

所有切片完成 Review 和提交后，任务才算完成。
