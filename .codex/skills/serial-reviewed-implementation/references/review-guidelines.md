# Static Review Guidelines

本文件定义 `serial-reviewed-implementation` Skill 中 review subagent 的静态代码审查方法。

目标是：

> 找出当前任务切片引入的、具体、真实、可操作的代码缺陷和回归风险，同时复用主 agent 已完成的动态验证结果。

## 1. Review 定位

这是一次独立、defect-first 的静态代码审查。

Review 聚焦：

- correctness；
- regression；
- 边界条件；
- 错误处理；
- 状态一致性；
- 生命周期；
- 并发与竞态；
- 接口契约；
- 数据格式与兼容性；
- 安全问题；
- 明显性能问题；
- 资源管理；
- 测试缺口；
- 与项目架构和明确规范的冲突。

Review 关注当前任务切片引入的问题。

## 2. 动态验证职责边界

主 agent 是动态验证的统一执行者。

review subagent 使用主 agent 提供的 build、test、lint、typecheck、benchmark 和运行时验证结果作为已经完成的证据。

review subagent 通过静态方式开展 Review，包括：

- 阅读完整 diff；
- 阅读 surrounding code；
- 搜索仓库；
- 查找 callers、references 和 implementations；
- 阅读接口和数据结构定义；
- 阅读相关测试代码；
- 阅读项目规则、文档和 CI 配置；
- 使用只读 Git 信息理解变更历史和范围。

如果一个重要判断确实需要额外动态证据，返回 `VERIFICATION_REQUIRED`，由主 agent 执行最小、针对性的验证。

## 3. Review 步骤

### 3.1 读取项目规则

确定 changed files 适用的 `AGENTS.md`、`AGENTS.override.md`、项目开发文档、代码规范、架构约束、测试约定和当前任务要求。

按项目自身的作用域和优先级解释这些规则。

### 3.2 确认 Review 范围

明确：

- Review 基准；
- 当前切片目标；
- 当前切片最新完整 diff；
- staged / unstaged / untracked 中属于当前切片的内容；
- 需要排除的既有修改。

Review 当前切片的完整变化，而不是只查看部分文件。

### 3.3 阅读完整 diff

先完整浏览当前切片 diff，理解：

- 修改意图；
- changed behavior；
- 新增和删除的路径；
- 接口变化；
- 状态变化；
- 测试变化。

形成整体模型后再深入具体文件。

### 3.4 阅读必要上下文

对每个关键改动，按需要查看：

- 所在函数、类、模块；
- 调用方；
- 被调用方；
- 接口实现；
- 共享状态；
- 配置；
- 序列化 / 持久化格式；
- 生命周期；
- 异常路径；
- 并发路径；
- 相关测试。

以能够证明或排除问题为目标读取上下文。

### 3.5 验证潜在 finding

报告 finding 前确认：

1. 问题由当前切片引入或被当前切片实质触发。
2. 存在具体受影响路径或场景。
3. 问题具有实际 correctness、regression、security、performance、resource 或 maintainability 影响。
4. 作者知道后通常会希望在提交前处理。
5. 结论有足够代码证据支持。

对于跨模块影响，找到实际受影响的调用点、接口或数据流后再报告。

### 3.6 检查剩余 diff

发现一个问题后继续检查完整 diff。

Review 目标是返回所有符合标准的 actionable findings，而不是找到第一个问题就结束。

## 4. Finding 标准

高质量 finding 应：

- 具体；
- 可定位；
- 可操作；
- 解释实际失败条件；
- 说明影响；
- 使用尽可能短而准确的位置范围。

推荐格式：

```text
[P1] path/to/file.ext:120-126 — 简短标题

<说明什么有问题、在什么条件下触发、为什么会产生实际影响。>
```

优先级可按影响使用：

- `P0`：阻断性、灾难性或广泛不可接受的问题；
- `P1`：高优先级，应在提交前修复；
- `P2`：正常优先级的真实缺陷；
- `P3`：低优先级但仍值得修复的具体问题。

如果难以可靠判断优先级，可以省略。

## 5. Review 判断原则

### 5.1 具体缺陷优先

优先报告：

- 明确错误行为；
- 确定的回归；
- 未处理边界；
- 错误状态转换；
- 错误生命周期；
- 资源泄漏；
- 并发竞态；
- 接口契约破坏；
- 数据损坏或兼容性问题；
- 安全缺陷；
- 明显且可证明的性能退化。

### 5.2 证据优先

Review 结论基于可检查证据。

当一个风险依赖于尚未确认的调用路径、输入条件或运行状态时，先调查仓库中的相关代码。

如果静态代码无法提供足够证据，而该问题对提交决策很重要，使用 `VERIFICATION_REQUIRED` 请求针对性验证。

### 5.3 作者意图与任务目标

结合当前任务目标、验收标准、changed code 和项目规则判断行为变化是否为有意设计。

与设计目标冲突或造成实际缺陷时，形成 finding。

### 5.4 项目规范

明确违反项目规则且影响 correctness、可维护性、架构一致性或后续开发安全性时，可以形成 finding。

普通个人风格偏好保持在 Review 范围之外。

## 6. 测试代码的审查方式

review subagent 阅读测试代码以判断：

- 当前改动是否已有回归覆盖；
- 测试是否真正覆盖 changed behavior；
- 断言是否足以捕获问题；
- 测试是否与实现契约一致；
- 是否存在重要但未覆盖的场景。

动态执行由主 agent 统一负责。

如果发现关键场景需要实际运行才能确定结果，返回 `VERIFICATION_REQUIRED`。

## 7. `VERIFICATION_REQUIRED` 标准

使用 `VERIFICATION_REQUIRED` 时，提出最小、针对性的验证请求。

格式：

```text
VERIFICATION_REQUIRED

1. Scenario:
   <需要验证的场景>

   Why:
   <为什么静态证据不足且该结果影响 Review 判断>

   Suggested validation:
   <最小、针对性的命令或测试范围>
```

验证请求聚焦当前切片的重要不确定性。

主 agent 返回验证结果后，重新执行完整静态 Review。

## 8. 复审规则

同一任务切片的复审继续由同一个 review subagent 完成。

每轮复审：

1. 重新读取最新完整 diff。
2. 检查上一轮 findings 的处理结果。
3. 重新评估所有 changed behavior。
4. 检查修复是否引入新缺陷。
5. 检查新增动态验证证据。
6. 继续搜索此前未发现的问题。
7. 以当前最新完整 diff 重新作出整体结论。

上一轮 findings 是上下文，不是本轮 Review 的范围。

## 9. 未采纳 finding

主 agent 可能提供未采纳 finding 的依据。

重新检查：

- 该依据是否能由代码、接口、测试或项目规则支持；
- finding 的触发条件是否仍存在；
- 影响是否仍成立。

依据充分时更新判断；依据不足时保留 finding，并具体说明剩余问题。

## 10. 输出协议

每轮只返回以下四种结果之一。

### PASS

```text
PASS

Reviewed:
- <Review 基准和范围简述>

No actionable findings.
No additional dynamic verification required.
```

### CHANGES_REQUIRED

```text
CHANGES_REQUIRED

Findings:
1. [P1] path:line — title
   <问题、触发条件和影响>

2. [P2] ...
```

### VERIFICATION_REQUIRED

```text
VERIFICATION_REQUIRED

No confirmed code defect requires modification yet.

Verification requests:
1. <针对性验证请求>
```

如果同时存在已经确认的代码缺陷和验证请求，使用 `CHANGES_REQUIRED`，并在 findings 后附加 `Verification requests`。

### REVIEW_INVALID

```text
REVIEW_INVALID

Reason:
<为什么当前无法完成可信 Review>

Needed:
<恢复 Review 所需的信息或动作>
```

## 11. 汇报给主 agent 的信息密度

主 agent 需要的是可行动结论。

汇报中保留：

- 最终状态；
- 所有 actionable findings；
- 必要的定位；
- 触发场景和影响；
- 必要的验证请求；
- 影响 Review 有效性的关键信息。

详细源码阅读过程、搜索过程和中间推理保留在 review subagent 自身上下文中。
