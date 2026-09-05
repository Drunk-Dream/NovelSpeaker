---
name: serial-reviewed-implementation
description: 严格串行完成代码任务切片：当前执行线程负责实现和验证，每个切片使用 Codex 内置 Review 模式进行独立代码审查，循环修复并重新审查最新完整 diff，审查通过后再进行原子提交。适用于要求“实现—验证—审查—修复—复审—提交”流程的代码修改任务；不适用于纯代码审查或明确禁止提交的任务。用户明确提及使用本 skill 时，即视为已授权本 skill 所执行任务的原子提交。
---

# 串行实现、Codex Review 与提交

严格按本 Skill 执行所选代码任务。不得跳过审查，不得在审查通过前提交，不得并行处理多个任务切片。

## 核心原则

- 当前执行线程负责需求理解、实现、验证、修复和提交。
- 代码审查必须使用 Codex 内置 Review 模式，不依赖项目自定义 `code_reviewer` 或其他审查 subagent。
- 每轮审查都视为一次新的、独立的 Review；不得依赖上一轮 reviewer 的会话记忆。
- 复审必须重新检查当前切片的最新完整 diff，不得只检查上一轮指出的问题。
- Review 负责发现代码问题；build、format、lint、typecheck、test 等验证仍由当前执行线程负责。
- 只有最终完整 diff 获得有效审查通过后，才能提交。

## 前置检查

开始前：

1. 确认当前目录位于 Git 仓库中。
2. 确认当前环境可调用 Codex CLI 的内置 Review 模式。
3. 读取适用的项目说明、`AGENTS.md`、开发规范和任务要求。
4. 记录初始 `git status`，识别任务开始前已经存在的修改。
5. 不得覆盖、清理、暂存、提交或以 `reset`、`checkout`、`restore`、`stash` 等方式扰动与当前任务无关的既有修改。
6. 将任务拆分为边界清晰、可独立实现、验证、审查和提交的任务切片。
7. 为每个切片明确 review 基准、改动范围以及如何获得仅属于该切片的完整 diff。

如果任务本身足够小且逻辑上不可再拆分，可以只创建一个任务切片。

如果当前任务必须修改一个已经包含无关未提交修改的文件，且无法可靠区分任务切片自身的改动，则不得让 Review 混合审查这些修改。优先使用独立临时 worktree 隔离当前切片；如果仍无法可靠构造仅属于当前切片的 review diff，则停止该切片的提交流程并报告该阻塞，不得以污染的 diff 代替独立审查。

## 串行约束

- 任何时刻只能处理一个任务切片。
- 当前切片完成实现、验证、审查和提交之前，不得开始下一切片。
- 不得并行运行多个 Review。
- 每个任务切片至少执行一次独立 Codex Review。
- 每次修复影响代码行为、接口、配置、数据格式或测试语义后，必须重新执行一次新的完整 Review。
- 新一轮 Review 必须以当前最新完整 diff 为审查对象，不得缩小为上一轮 findings 的局部修复。
- Review 失败、被中断、范围不明确、输出不完整或无法判断最终结论时，视为“审查无效”，不得提交；必须重新运行有效 Review。

## 提交授权

- 用户明确提及使用本 skill（例如要求“按该 skill 执行”或“使用 serial-reviewed-implementation”），即视为用户已授权本 skill 所执行任务切片的原子提交；提交前无需再单独请求授权。
- 该授权仅覆盖本 skill 流程产生的切片提交，不延伸至打标签、推送、创建 Release、创建 PR、合并 PR 或修改其他远端内容；这些操作仍需用户单独明确授权。

## 每个任务切片的执行流程

### 1. 定义切片

在修改代码前，明确：

- 当前切片的目标；
- 范围和不包含的内容；
- 验收标准；
- 预计涉及的文件或模块；
- 适用的验证方式；
- review 基准；
- 当前切片的预期 diff 范围；
- 任务开始前已存在且不得混入当前切片的修改。

切片范围应足够小，使其 diff 可以独立理解、验证、审查和提交。

### 2. 实现

由当前执行线程完成当前切片的全部代码修改。

要求：

- 遵循现有架构、接口和项目约定；
- 只修改完成当前切片所必需的内容；
- 补充或调整必要的测试代码；
- 不得混入无关重构、格式变化或清理工作；
- 持续检查工作区，避免影响任务开始前已有的修改；
- 如果发现当前实现需要扩大切片范围，先重新界定当前切片，再继续修改。

### 3. 验证

当前执行线程负责执行与改动相关的验证，包括适用的：

- build；
- format；
- lint；
- typecheck；
- test；
- benchmark；
- 其他项目规定的检查。

只运行与当前切片合理相关的验证，记录执行命令和结果。发现问题时先修复并重新验证。

在进入 Review 前，必须：

- 实现已满足验收标准；
- 必要验证已完成；
- 检查完整 `git diff` 和 `git status`；
- 确认当前切片没有混入无关修改；
- 确定供 Review 使用的完整 diff 范围和基准；
- 确认 untracked 文件中属于当前切片的新增文件也被纳入审查。

### 4. 准备独立 Review 范围

Review 必须能够只针对当前切片进行判断。

#### 情况 A：当前工作区的待审查修改仅属于当前切片

可以直接在当前工作区运行 Codex 内置 Review。

审查前再次确认：

- staged、unstaged 和相关 untracked 修改均属于当前切片；
- 不存在会干扰审查判断的无关修改；
- review 基准正确。

#### 情况 B：当前工作区存在与切片无关的既有修改

不得直接把整个工作区交给 Review。

优先创建临时 detached worktree，并仅将当前切片的改动放入该 worktree：

1. 以当前切片的 Git 基准创建临时 worktree。
2. 只应用当前切片的 patch 和当前切片新增文件。
3. 不复制、不应用任务开始前已有的无关修改。
4. 检查临时 worktree 的 `git status` 和完整 diff。
5. 确认其中所有未提交修改都属于当前切片。
6. 在该临时 worktree 中运行 Codex Review。
7. Review 完成后删除临时 worktree；不得借此改变原工作区中的用户修改。

如果无法可靠生成仅包含当前切片的 patch，则不得继续 Review 或提交。

### 5. 运行 Codex 内置 Review

优先使用非交互式 Codex Review，以便当前执行线程能够完整读取审查结果。

推荐形式：

```bash
codex exec --sandbox read-only review "<review instructions>"
```

`review instructions` 必须清楚说明：

- 当前切片的目标和必要背景；
- 验收标准；
- 当前切片的改动范围；
- review 基准；
- 如何查看当前切片的完整 diff；
- 哪些既有修改不属于当前切片并且不得作为 finding；
- 已执行的验证命令及结果，仅作为背景信息；
- 需要检查完整改动，而不是只检查某几个文件或上一轮 findings；
- 重点检查 correctness、regression、边界条件、错误处理、状态一致性、接口契约、安全性、性能风险以及测试缺口；
- 只提出由当前切片引入、具体且可操作的问题；
- 不修改任何文件；
- 不把 Reviewer 自己可能执行的命令或检查视为实现线程验证的替代；
- 给出明确的整体正确性结论。

如果当前 Codex CLI 支持适合当前场景的原生范围参数，也可以使用对应的内置 Review target，例如仅在确认整个未提交工作区都属于当前切片时使用：

```bash
codex exec --sandbox read-only review --uncommitted
```

不得为了方便而使用 `--uncommitted` 审查包含无关既有修改的工作区。

当使用 `--uncommitted`、`--base` 或 `--commit` 等原生 Review target 时，如果当前 CLI 不允许同时附加自定义 review instructions，则以“审查范围正确”为优先级，不得通过错误组合参数规避 CLI 限制。

Review 命令成功退出不等于审查通过。必须读取完整 Review 结果并判断其内容。

### 6. 归一化审查结论

当前执行线程将 Codex Review 的原生结果归一化为以下三类之一。

#### `PASS`

仅当同时满足以下条件时：

- Review 已完整结束且输出有效；
- Review 确实针对当前切片的最新完整 diff；
- 没有需要修改的 actionable finding；
- 整体正确性结论明确认为当前 patch 正确、可接受或等价表述；
- 没有尚未解决的 correctness、regression、接口、状态一致性、安全性或明显测试缺口。

不得因为只有低优先级 finding 就自动判定 `PASS`。只要 finding 指向当前切片引入的真实缺陷或应当在提交前修复的问题，就必须处理。

#### `CHANGES_REQUIRED`

出现以下任一情况：

- Review 给出一个或多个成立的 actionable finding；
- Review 认为 patch 不正确或存在可能导致错误行为的风险；
- Review 指出当前验收标准没有被满足；
- Review 指出测试遗漏会导致当前改动无法安全提交；
- 其他明确要求在提交前修改的问题。

#### `REVIEW_INVALID`

出现以下任一情况：

- Review 命令失败或被中断；
- Review 输出明显不完整；
- 无法判断 Reviewer 的整体结论；
- Reviewer 审查了错误的 diff、错误的基准或混入无关修改；
- Review 所依据的工作区在审查期间发生了变化；
- 当前执行线程无法确认 Review 对应的是当前切片的最终完整 diff。

`REVIEW_INVALID` 不得视为通过，必须修正原因并重新执行新的完整 Review。

### 7. 处理 Review findings

#### 返回 `CHANGES_REQUIRED`

逐项处理审查意见：

1. 验证 finding 是否成立。
2. 修复所有成立的问题。
3. 对不成立的 finding，记录具体代码依据、设计约束或可验证事实，不得直接忽略。
4. 重新运行受修改影响的必要验证。
5. 再次检查当前切片的完整 `git diff` 和 `git status`。
6. 确认修复没有混入无关改动。
7. 重新运行一次新的 Codex Review。
8. 新 Reviewer 必须重新审查最新完整 diff，而不是仅检查上一轮 findings。
9. 将上一轮未采纳 finding 的依据作为本轮 Review 背景的一部分；但不得要求新 Reviewer 直接继承上一轮结论。
10. 重复“修改—验证—完整 Review”，直到获得有效 `PASS`。

不得由当前执行线程自行绕过 Reviewer finding 后宣布审查通过。

#### 返回 `REVIEW_INVALID`

修复审查环境、范围、命令或输出问题后，重新执行完整 Review。

#### 返回 `PASS`

确认该 `PASS` 对应当前切片的最终完整 diff。

如果 `PASS` 之后发生任何可能影响以下内容的修改，则该结论立即失效：

- 代码行为；
- 接口；
- 配置；
- 数据格式；
- 依赖关系；
- 测试语义；
- 构建逻辑。

必须完成必要验证，并重新执行新的完整 Codex Review，直到再次获得有效 `PASS`。

仅注释、文档或纯格式修改是否需要重新 Review，应保守判断：如果它可能改变生成结果、API 文档契约、配置含义、测试输入或代码解析结果，则必须重新 Review。

### 8. 原子提交

只有在最终完整 diff 获得有效 `PASS` 后才能提交。

提交前：

1. 再次检查 `git status` 和完整 diff。
2. 确保没有混入任务开始前的修改或其他切片的改动。
3. 确认提交内容与最后一次通过 Review 的内容一致。
4. 按逻辑将当前切片拆分为一个或多个原子提交。

原子提交要求：

- 每个 commit 只有一个明确目的；
- 每个 commit 的改动逻辑完整；
- 不同模块、性质或关注点的修改尽量分开；
- 每个 commit 可独立理解和审查；
- commit message 简洁、准确并描述目的；
- 不得使用一次性提交掩盖多个无关改动。

可使用交互式暂存或按文件、按补丁暂存来组织提交，但不得误暂存既有无关修改。

如果暂存、提交钩子或提交准备过程修改了文件，必须：

1. 停止后续提交操作；
2. 检查新增修改；
3. 重新运行必要验证；
4. 再次检查最终完整 diff；
5. 重新执行新的完整 Codex Review；
6. 再次获得有效 `PASS` 后再完成提交。

如果计划拆成多个 commits，而最后一次 Review 审查的是这些 commits 的整体组合，则允许在不改变文件内容的前提下按原子原则暂存和提交；任何因拆分提交而产生的内容变化都会使 Review 失效。

### 9. 完成切片

只有同时满足以下条件，当前切片才算完成：

- 实现满足验收标准；
- 必要验证已通过；
- Codex 内置 Review 已对最终完整 diff 给出有效 `PASS`；
- 所有成立的 Review findings 均已处理；
- 全部修改已按原子提交原则提交；
- 提交内容与最后一次通过 Review 的内容一致；
- 工作区中不存在属于当前切片的未提交修改；
- 任务开始前已有的无关修改仍保持原状。

满足后才能开始下一任务切片。下一切片重新执行完整流程，并使用新的独立 Codex Review。

## Review 指令模板

当使用自定义 Codex Review target 时，可按以下结构构造本轮指令。根据当前切片替换占位内容，不要机械照抄无关项目。

```text
Review the current task slice as an independent code reviewer.

Task:
<当前切片目标>

Acceptance criteria:
<验收标准>

Scope:
<当前切片改动范围>

Diff baseline and inspection:
<如何查看当前切片的最新完整 diff>

Pre-existing changes to exclude:
<不存在则写 none；存在时明确列出>

Validation already performed by the implementation thread:
<命令和结果>

Review requirements:
- Review the complete current diff, not only selected files or previous findings.
- Flag only concrete, actionable issues introduced by this task slice.
- Focus on correctness, regressions, boundary conditions, error handling,
  state consistency, interface contracts, security, performance risks,
  and meaningful test gaps.
- Do not modify files.
- Treat validation results above only as context; do not replace the
  implementation thread's validation responsibility.
- Provide an explicit overall correctness verdict.
```

对于复审，在上述内容中额外加入上一轮 findings 及其处理结果，但仍要求本轮从最新完整 diff 独立判断：

```text
Previous review findings and disposition:
<finding -> fixed / not adopted with concrete rationale>

Re-review the entire latest diff independently. Do not limit the review
to the previous findings, and check whether the fixes introduced new issues.
```

## 任务完成

全部任务切片完成后，汇总：

- 已完成的任务切片；
- 每个切片执行的关键验证；
- 每个切片最终有效 Review 的结论；
- 主要 Review findings 及处理结果；
- 创建的 commits 及其目的；
- 仍存在但不属于本任务范围的既有工作区修改或风险。

不得在任何任务切片尚未获得有效 Review `PASS` 或尚未提交时宣告整个任务完成。
